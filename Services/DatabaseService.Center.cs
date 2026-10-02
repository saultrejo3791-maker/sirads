using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIRASD.Desktop.Models;

namespace SIRASD.Desktop.Services;

public sealed partial class DatabaseService
{
    private static void InitializeCenter(SqliteConnection c)
    {
        using var q = c.CreateCommand();
        q.CommandText = """
            CREATE TABLE IF NOT EXISTS CenterRecords(
              Id TEXT PRIMARY KEY, Kind TEXT NOT NULL, PatientId TEXT REFERENCES Patients(Id),
              Title TEXT NOT NULL, OccurredAt TEXT NOT NULL, Responsible TEXT NOT NULL, Fields TEXT NOT NULL,
              Version INTEGER NOT NULL, CreatedAt TEXT NOT NULL, CreatedBy TEXT NOT NULL, UpdatedAt TEXT NOT NULL, UpdatedBy TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_CenterRecords_KindDate ON CenterRecords(Kind,OccurredAt);
            CREATE INDEX IF NOT EXISTS IX_CenterRecords_Patient ON CenterRecords(PatientId);
            CREATE TABLE IF NOT EXISTS CenterRecordHistory(
              RecordId TEXT NOT NULL REFERENCES CenterRecords(Id), Version INTEGER NOT NULL, Snapshot TEXT NOT NULL,
              RecordedAt TEXT NOT NULL, RecordedBy TEXT NOT NULL, PRIMARY KEY(RecordId,Version));
            CREATE TABLE IF NOT EXISTS PatientEditHistory(
              Id TEXT PRIMARY KEY, PatientId TEXT NOT NULL REFERENCES Patients(Id), BeforeJson TEXT NOT NULL,
              AfterJson TEXT NOT NULL, RecordedAt TEXT NOT NULL, RecordedBy TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS PantryItems(
              Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Category TEXT NOT NULL CHECK(Category IN ('Alimentos','Limpieza')),
              Unit TEXT NOT NULL, Location TEXT NOT NULL, Stock INTEGER NOT NULL CHECK(Stock>=0 AND Stock<=999999999000),
              Minimum INTEGER NOT NULL CHECK(Minimum>=0 AND Minimum<=999999999000), Version INTEGER NOT NULL,
              UNIQUE(Name,Category,Unit));
            CREATE TABLE IF NOT EXISTS PantryMovements(
              Id TEXT PRIMARY KEY, ItemId TEXT NOT NULL REFERENCES PantryItems(Id), Type TEXT NOT NULL CHECK(Type IN ('Entrada','Salida')),
              Quantity INTEGER NOT NULL CHECK(Quantity>0), Balance INTEGER NOT NULL CHECK(Balance>=0),
              Reference TEXT NOT NULL, Responsible TEXT NOT NULL, OccurredAt TEXT NOT NULL, RecordedBy TEXT NOT NULL);
            """;
        q.ExecuteNonQuery();
        EnablePantryLosses(c);
    }

    private static SqliteCommand CenterCommand(SqliteConnection c, SqliteTransaction? t, string sql, params (string,object?)[] args)
    {
        var q=c.CreateCommand(); q.Transaction=t; q.CommandText=sql;
        foreach(var (name,value) in args) q.Parameters.AddWithValue(name,value ?? DBNull.Value);
        return q;
    }
    private static void RequireText(string? value,string label) { if(string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Completa: {label}."); }
    private static string Iso(DateTime date) => date.ToUniversalTime().ToString("O");
    private static DateTime Local(string value) => DateTime.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToLocalTime();
    private static long PantryUnits(decimal value)
    {
        if(value<0 || value>999999999 || decimal.Round(value,3)!=value) throw new InvalidOperationException("Usa una cantidad entre 0 y 999999999 con hasta 3 decimales.");
        return checked((long)(value*1000));
    }

    public void UpdatePatient(Patient p, Patient original, string user)
    {
        RequireText(user,"usuario"); RequireText(p.FirstName,"nombre"); RequireText(p.Folio,"folio");
        if(p.Id!=original.Id || p.ExternalId!=original.ExternalId) throw new InvalidOperationException("La identidad del paciente no puede cambiar.");
        if(!DateTime.TryParseExact(p.AdmissionDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)) throw new InvalidOperationException("Fecha de ingreso inválida.");
        if(p.WeeklyFee<0 || p.WeeklyFee>999999999 || decimal.Round(p.WeeklyFee,2)!=p.WeeklyFee) throw new InvalidOperationException("Cuota inválida. Usa hasta dos decimales.");
        if(!new[]{"Activo","Egresado","Baja"}.Contains(p.Status) && p.Status!=original.Status) throw new InvalidOperationException("Estado inválido.");
        if(!new[]{"Verde","Amarillo","Rojo"}.Contains(p.TrafficLight) && p.TrafficLight!=original.TrafficLight) throw new InvalidOperationException("Semáforo inválido.");
        using var c=Open(); using var t=c.BeginTransaction(deferred:false);
        using var select=CenterCommand(c,t,"SELECT Id,ExternalId,Folio,FirstName,LastName,LastName2,AdmissionDate,Status,TrafficLight,WeeklyFee,Guardian,Phone,Notes FROM Patients WHERE Id=$id AND DeletedAt IS NULL",("$id",p.Id));
        Patient current;
        using(var reader=select.ExecuteReader()) { if(!reader.Read()) throw new InvalidOperationException("El paciente ya no está disponible."); current=ReadPatient(reader); }
        if(JsonSerializer.Serialize(current)!=JsonSerializer.Serialize(original)) throw new InvalidOperationException("El paciente cambió en otra ventana. Cierra y vuelve a abrir su registro.");
        using var q=CenterCommand(c,t,"UPDATE Patients SET Folio=$folio,FirstName=$first,LastName=$last,LastName2=$last2,AdmissionDate=$date,Status=$status,TrafficLight=$traffic,WeeklyFee=$fee,Guardian=$guardian,Phone=$phone,Notes=$notes,UpdatedAt=$now WHERE Id=$id",
          ("$id",p.Id),("$folio",p.Folio.Trim()),("$first",p.FirstName.Trim()),("$last",p.LastName.Trim()),("$last2",p.LastName2.Trim()),("$date",p.AdmissionDate),("$status",p.Status),("$traffic",p.TrafficLight),("$fee",p.WeeklyFee),("$guardian",p.Guardian),("$phone",p.Phone),("$notes",p.Notes),("$now",Iso(DateTime.Now)));
        try { q.ExecuteNonQuery(); } catch(SqliteException ex) when(ex.SqliteErrorCode==19) { throw new InvalidOperationException("El folio ya pertenece a otro paciente.",ex); }
        using var history=CenterCommand(c,t,"INSERT INTO PatientEditHistory VALUES($id,$patient,$before,$after,$now,$user)",("$id",Guid.NewGuid().ToString()),("$patient",p.Id),("$before",JsonSerializer.Serialize(current)),("$after",JsonSerializer.Serialize(p)),("$now",Iso(DateTime.Now)),("$user",user)); history.ExecuteNonQuery();
        ReleaseInactiveBeds(c,t,user,p.Id);
        Audit(c,user,"Paciente actualizado",p.Folio,t); t.Commit();
    }

    public string GetProfileName(string email)
    {
        using var c=Open(); using var q=CenterCommand(c,null,"SELECT Name FROM Users WHERE Email=$email AND Active=1",("$email",email.Trim()));
        return Convert.ToString(q.ExecuteScalar()) ?? "";
    }
    public void UpdateProfileName(string email,string name,string oldName)
    {
        RequireText(name,"nombre del usuario"); if(name.Trim().Length>100) throw new InvalidOperationException("El nombre admite hasta 100 caracteres.");
        using var c=Open(); using var t=c.BeginTransaction(deferred:false);
        using var q=CenterCommand(c,t,"UPDATE Users SET Name=$name,UpdatedAt=$now WHERE Email=$email AND Name=$old AND Active=1",("$name",name.Trim()),("$now",Iso(DateTime.Now)),("$email",email.Trim()),("$old",oldName));
        if(q.ExecuteNonQuery()!=1) throw new InvalidOperationException("El perfil cambió. Inicia sesión nuevamente.");
        Audit(c,oldName,"Nombre de usuario actualizado",$"{oldName} → {name.Trim()}",t);t.Commit();
    }

    public List<CenterRecord> GetCenterRecords(string kind)
    {
        _=CenterModules.Get(kind);
        using var c=Open();using var q=CenterCommand(c,null,"""
            SELECT r.Id,r.Kind,r.PatientId,r.Title,r.OccurredAt,r.Responsible,r.Fields,r.Version,r.CreatedAt,r.CreatedBy,r.UpdatedAt,r.UpdatedBy,
              COALESCE(p.Folio||' · '||TRIM(p.FirstName||' '||p.LastName||' '||p.LastName2),'Sin paciente vinculado')
            FROM CenterRecords r LEFT JOIN Patients p ON p.Id=r.PatientId WHERE r.Kind=$kind ORDER BY r.OccurredAt DESC
            """,("$kind",kind));
        using var reader=q.ExecuteReader();var result=new List<CenterRecord>();
        while(reader.Read()) result.Add(new(){Id=reader.GetString(0),Kind=reader.GetString(1),PatientId=reader.IsDBNull(2)?null:reader.GetString(2),Title=reader.GetString(3),OccurredAt=Local(reader.GetString(4)),Responsible=reader.GetString(5),Fields=JsonSerializer.Deserialize<Dictionary<string,string>>(reader.GetString(6))!,Version=reader.GetInt32(7),CreatedAt=Local(reader.GetString(8)),CreatedBy=reader.GetString(9),UpdatedAt=Local(reader.GetString(10)),UpdatedBy=reader.GetString(11),PatientName=reader.GetString(12)});
        return result;
    }

    public void SaveCenterRecord(CenterRecord record,string user)
    {
        var module=CenterModules.Get(record.Kind);
        RequireText(user,"usuario");RequireText(record.Title,"título / motivo breve");RequireText(record.Responsible,"responsable");
        if(record.OccurredAt.Year<1900) throw new InvalidOperationException("Fecha inválida.");
        if(module.PatientRequired) RequireText(record.PatientId,"paciente");
        foreach(var field in module.Fields)
        {
            var value=record.Fields.GetValueOrDefault(field.Key,"");
            if(field.Required) RequireText(value,field.Label);
            if(string.IsNullOrWhiteSpace(value)) continue;
            if(field.Options!=null && !field.Options.Contains(value)) throw new InvalidOperationException($"Opción inválida: {field.Label}.");
            if(field.Type=="date" && !DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)) throw new InvalidOperationException($"Fecha inválida: {field.Label}.");
            if(field.Type=="datetime" && !DateTime.TryParseExact(value,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)) throw new InvalidOperationException($"Fecha y hora inválidas: {field.Label}.");
            if(field.Type=="money" && (!decimal.TryParse(value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n) || n<0 || n>999999999 || decimal.Round(n,2)!=n)) throw new InvalidOperationException("Monto inválido. Usa punto decimal, sin separadores de miles y hasta dos decimales.");
        }
        if(record.Kind=="Calendario")
        {
            if(record.Type=="Fecha de pago") {RequireText(record.PatientId,"paciente para la fecha de pago");RequireText(record.Fields.GetValueOrDefault("amount"),"monto previsto");}
            else if(record.Status=="Pagado") throw new InvalidOperationException("El estado Pagado corresponde a una fecha de pago.");
        }
        using var c=Open();using var t=c.BeginTransaction(deferred:false);
        if(!string.IsNullOrWhiteSpace(record.PatientId))
        {
            using var patient=CenterCommand(c,t,"SELECT Folio||' · '||TRIM(FirstName||' '||LastName||' '||LastName2) FROM Patients WHERE Id=$id AND DeletedAt IS NULL",("$id",record.PatientId));
            var name=patient.ExecuteScalar() as string;
            if(name==null) throw new InvalidOperationException("Selecciona un paciente existente.");record.PatientName=name;
        }
        var next=record.Copy(); next.Version++; next.UpdatedAt=DateTime.Now;next.UpdatedBy=user;
        if(record.Version==0){next.CreatedAt=next.UpdatedAt;next.CreatedBy=user;}
        else
        {
            using var original=CenterCommand(c,t,"SELECT CreatedAt,CreatedBy FROM CenterRecords WHERE Id=$id AND Kind=$kind AND Version=$version",("$id",record.Id),("$kind",record.Kind),("$version",record.Version));
            using var r=original.ExecuteReader(); if(!r.Read())throw new InvalidOperationException("El registro cambió en otra ventana. Actualiza la lista y vuelve a abrirlo.");next.CreatedAt=Local(r.GetString(0));next.CreatedBy=r.GetString(1);
        }
        using var q=CenterCommand(c,t,record.Version==0
            ? "INSERT INTO CenterRecords VALUES($id,$kind,$patient,$title,$date,$responsible,$fields,$next,$created,$creator,$now,$user)"
            : "UPDATE CenterRecords SET PatientId=$patient,Title=$title,OccurredAt=$date,Responsible=$responsible,Fields=$fields,Version=$next,UpdatedAt=$now,UpdatedBy=$user WHERE Id=$id AND Kind=$kind AND Version=$previous",
            ("$id",next.Id),("$kind",next.Kind),("$patient",string.IsNullOrWhiteSpace(next.PatientId)?null:next.PatientId),("$title",next.Title.Trim()),("$date",Iso(next.OccurredAt)),("$responsible",next.Responsible.Trim()),("$fields",JsonSerializer.Serialize(next.Fields)),("$next",next.Version),("$previous",record.Version),("$created",Iso(next.CreatedAt)),("$creator",next.CreatedBy),("$now",Iso(next.UpdatedAt)),("$user",user));
        if(q.ExecuteNonQuery()!=1) throw new InvalidOperationException("No fue posible guardar el registro.");
        using var history=CenterCommand(c,t,"INSERT INTO CenterRecordHistory VALUES($id,$version,$snapshot,$now,$user)",("$id",next.Id),("$version",next.Version),("$snapshot",JsonSerializer.Serialize(next)),("$now",Iso(next.UpdatedAt)),("$user",user));history.ExecuteNonQuery();
        Audit(c,user,record.Version==0?"Registro creado":"Registro actualizado",$"{module.Title}: {next.Id}",t);t.Commit();
        record.Version=next.Version;record.CreatedAt=next.CreatedAt;record.CreatedBy=next.CreatedBy;record.UpdatedAt=next.UpdatedAt;record.UpdatedBy=user;
    }
    public List<CenterRecord> GetCenterHistory(string id)
    {
        using var c=Open();using var q=CenterCommand(c,null,"SELECT Snapshot FROM CenterRecordHistory WHERE RecordId=$id ORDER BY Version DESC",("$id",id));using var r=q.ExecuteReader();var list=new List<CenterRecord>();while(r.Read())list.Add(JsonSerializer.Deserialize<CenterRecord>(r.GetString(0))!);return list;
    }

    public List<PantryItem> GetPantryItems()
    {
        using var c=Open();using var q=CenterCommand(c,null,"SELECT Id,Name,Category,Unit,Location,Stock,Minimum,Version FROM PantryItems ORDER BY Category,Name");using var r=q.ExecuteReader();var list=new List<PantryItem>();
        while(r.Read())list.Add(new(){Id=r.GetString(0),Name=r.GetString(1),Category=r.GetString(2),Unit=r.GetString(3),Location=r.GetString(4),Stock=r.GetInt64(5)/1000m,Minimum=r.GetInt64(6)/1000m,Version=r.GetInt32(7)});return list;
    }
    public void SavePantryItem(PantryItem item,string user)
    {
        RequireText(user,"usuario");RequireText(item.Name,"artículo");RequireText(item.Unit,"unidad");if(!new[]{"Alimentos","Limpieza"}.Contains(item.Category))throw new InvalidOperationException("Categoría inválida.");var min=PantryUnits(item.Minimum);
        using var c=Open();using var t=c.BeginTransaction(deferred:false);
        using var q=CenterCommand(c,t,item.Version==0?"INSERT INTO PantryItems VALUES($id,$name,$category,$unit,$location,0,$min,1)":"UPDATE PantryItems SET Name=$name,Location=$location,Minimum=$min,Version=Version+1 WHERE Id=$id AND Version=$version AND Unit=$unit AND Category=$category",
            ("$id",item.Id),("$name",item.Name.Trim()),("$category",item.Category),("$unit",item.Unit.Trim()),("$location",item.Location.Trim()),("$min",min),("$version",item.Version));
        try {if(q.ExecuteNonQuery()!=1)throw new InvalidOperationException("El artículo cambió. Actualiza la lista. La unidad y categoría se conservan para proteger su historial.");}
        catch(SqliteException ex) when(ex.SqliteErrorCode==19){throw new InvalidOperationException("Ya existe un artículo con ese nombre, categoría y unidad.",ex);}
        Audit(c,user,"Artículo de inventario guardado",item.Id,t);t.Commit();item.Version++;
    }
    public void RecordPantryMovement(string operationId,string itemId,string type,decimal quantity,string reference,string responsible,string user)
    {
        RequireText(operationId,"operación");RequireText(reference,"origen / destino o motivo");RequireText(responsible,"responsable");RequireText(user,"usuario");
        var units=PantryUnits(quantity);if(units==0)throw new InvalidOperationException("La cantidad debe ser mayor que cero.");if(type!="Entrada"&&type!="Salida"&&type!="Baja")throw new InvalidOperationException("Movimiento inválido.");
        using var c=Open();using var t=c.BeginTransaction(deferred:false);
        using(var existing=CenterCommand(c,t,"SELECT ItemId,Type,Quantity,Reference,Responsible,RecordedBy FROM PantryMovements WHERE Id=$id",("$id",operationId)))
        using(var r=existing.ExecuteReader())if(r.Read()){if(r.GetString(0)!=itemId||r.GetString(1)!=type||r.GetInt64(2)!=units||r.GetString(3)!=reference||r.GetString(4)!=responsible||r.GetString(5)!=user)throw new InvalidOperationException("La operación ya existe con otros datos.");return;}
        using var update=CenterCommand(c,t,"UPDATE PantryItems SET Stock=Stock+$delta,Version=Version+1 WHERE Id=$id AND Stock+$delta BETWEEN 0 AND 999999999000 RETURNING Stock",("$id",itemId),("$delta",type=="Entrada"?units:-units));
        var balance=update.ExecuteScalar();if(balance==null)throw new InvalidOperationException("Existencia insuficiente, cantidad demasiado grande o artículo no disponible.");
        using var q=CenterCommand(c,t,"INSERT INTO PantryMovements VALUES($id,$item,$type,$quantity,$balance,$reference,$responsible,$now,$user)",("$id",operationId),("$item",itemId),("$type",type),("$quantity",units),("$balance",balance),("$reference",reference),("$responsible",responsible),("$now",Iso(DateTime.Now)),("$user",user));q.ExecuteNonQuery();
        Audit(c,user,"Movimiento de inventario",$"{type}: {itemId}",t);t.Commit();
    }
    public List<PantryMovement> GetPantryMovements()
    {
        using var c=Open();using var q=CenterCommand(c,null,"SELECT m.Id,i.Name,i.Category,m.Type,m.Quantity,m.Balance,i.Unit,m.Reference,m.Responsible,m.OccurredAt,m.RecordedBy,m.ItemId FROM PantryMovements m JOIN PantryItems i ON i.Id=m.ItemId ORDER BY m.OccurredAt DESC");using var r=q.ExecuteReader();var list=new List<PantryMovement>();while(r.Read())list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt64(4)/1000m,r.GetInt64(5)/1000m,r.GetString(6),r.GetString(7),r.GetString(8),Local(r.GetString(9)),r.GetString(10),r.GetString(11)));return list;
    }
}
