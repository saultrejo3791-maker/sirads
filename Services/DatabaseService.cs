using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIRASD.Desktop.Models;

namespace SIRASD.Desktop.Services;

public sealed partial class DatabaseService
{
    private readonly string _directory;
    public DatabaseService(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIRASD");
    }
    private string DatabasePath => Path.Combine(_directory, "SIRASD.db");
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString();

    public void Initialize()
    {
        Directory.CreateDirectory(_directory);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys=ON;
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Users(
                Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Email TEXT NOT NULL COLLATE NOCASE UNIQUE,
                PasswordHash TEXT NOT NULL, PasswordSalt TEXT NOT NULL, Role TEXT NOT NULL,
                Active INTEGER NOT NULL DEFAULT 1, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Patients(
                Id TEXT PRIMARY KEY, ExternalId TEXT UNIQUE, Folio TEXT NOT NULL UNIQUE,
                FirstName TEXT NOT NULL, LastName TEXT NOT NULL DEFAULT '', LastName2 TEXT NOT NULL DEFAULT '',
                AdmissionDate TEXT NOT NULL, Status TEXT NOT NULL, TrafficLight TEXT NOT NULL,
                WeeklyFee REAL NOT NULL DEFAULT 600, Guardian TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '', Notes TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, DeletedAt TEXT);
            CREATE TABLE IF NOT EXISTS Beds(
                Id TEXT PRIMARY KEY, Room TEXT NOT NULL, Code TEXT NOT NULL, Level TEXT NOT NULL,
                Locker TEXT NOT NULL DEFAULT '', Status TEXT NOT NULL DEFAULT 'Disponible',
                PatientId TEXT, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                UNIQUE(Room, Code, Level), FOREIGN KEY(PatientId) REFERENCES Patients(Id));
            CREATE TABLE IF NOT EXISTS BedHistory(
                Id TEXT PRIMARY KEY, BedId TEXT NOT NULL, PatientId TEXT,
                Action TEXT NOT NULL, Reason TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Incidents(
                Id TEXT PRIMARY KEY, PatientId TEXT, Type TEXT NOT NULL, Description TEXT NOT NULL,
                Status TEXT NOT NULL, OccurredAt TEXT NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS AuditLog(
                Id TEXT PRIMARY KEY, UserName TEXT NOT NULL, Action TEXT NOT NULL,
                Detail TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS AppMeta(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        InitializeMedications(connection);
        InitializeCenter(connection);
        InitializePayments(connection);
        SeedDirector(connection);
        SeedBeds(connection);
        using var sync = connection.BeginTransaction(deferred: false);
        ReleaseInactiveBeds(connection, sync, "Sistema · sincronización de bajas");
        sync.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private static void SeedDirector(SqliteConnection connection)
    {
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Users";
        if (Convert.ToInt32(count.ExecuteScalar()) > 0) return;
        var (hash, salt) = PasswordService.Hash("SIRASD2026");
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO Users VALUES($id,$name,$email,$hash,$salt,$role,1,$now,$now)";
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        insert.Parameters.AddWithValue("$name", "José Ismael");
        insert.Parameters.AddWithValue("$email", "direccion@asolascondios.org");
        insert.Parameters.AddWithValue("$hash", hash);
        insert.Parameters.AddWithValue("$salt", salt);
        insert.Parameters.AddWithValue("$role", "Dirección");
        insert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        insert.ExecuteNonQuery();
    }

    private static void SeedBeds(SqliteConnection connection)
    {
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Beds";
        if (Convert.ToInt32(count.ExecuteScalar()) > 0) return;
        var levels = new[] { "Inferior", "Medio", "Superior" };
        using var transaction = connection.BeginTransaction();
        for (var bunk = 1; bunk <= 10; bunk++)
        for (var level = 0; level < levels.Length; level++)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO Beds VALUES($id,'Dormitorio principal',$code,$level,$locker,'Disponible',NULL,$now,$now)";
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("$code", $"Litera {bunk:00}");
            insert.Parameters.AddWithValue("$level", levels[level]);
            insert.Parameters.AddWithValue("$locker", $"L-{((bunk - 1) * 3 + level + 1):00}");
            insert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public string? Authenticate(string email, string password)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name,PasswordHash,PasswordSalt FROM Users WHERE Email=$email AND Active=1";
        command.Parameters.AddWithValue("$email", email.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read() || !PasswordService.Verify(password, reader.GetString(1), reader.GetString(2))) return null;
        return reader.GetString(0);
    }

    public DashboardStats GetStats()
    {
        using var connection = Open();
        int Scalar(string sql) { using var c = connection.CreateCommand(); c.CommandText = sql; return Convert.ToInt32(c.ExecuteScalar()); }
        return new(Scalar("SELECT COUNT(*) FROM Patients WHERE Status='Activo' AND DeletedAt IS NULL"),
            Scalar("SELECT COUNT(*) FROM Beds WHERE Status='Ocupada'"),
            Scalar("SELECT COUNT(*) FROM Beds WHERE Status='Disponible'"),
            Scalar("SELECT COUNT(*) FROM Incidents WHERE Status<>'Cerrada'") + Scalar("SELECT COUNT(*) FROM CenterRecords WHERE Kind='Incidencias' AND json_extract(Fields,'$.status')<>'Cerrada'"));
    }

    public List<Patient> GetPatients()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,ExternalId,Folio,FirstName,LastName,LastName2,AdmissionDate,Status,TrafficLight,WeeklyFee,Guardian,Phone,Notes FROM Patients WHERE DeletedAt IS NULL ORDER BY AdmissionDate DESC, FullName".Replace("FullName", "LastName||FirstName");
        using var reader = command.ExecuteReader();
        var result = new List<Patient>();
        while (reader.Read()) result.Add(ReadPatient(reader));
        return result;
    }

    public List<Bed> GetBeds()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.Id,b.Room,b.Code,b.Level,b.Locker,b.Status,b.PatientId,
                   TRIM(COALESCE(p.FirstName,'')||' '||COALESCE(p.LastName,'')||' '||COALESCE(p.LastName2,''))
            FROM Beds b LEFT JOIN Patients p ON p.Id=b.PatientId
            ORDER BY b.Code, CASE b.Level WHEN 'Inferior' THEN 1 WHEN 'Medio' THEN 2 WHEN 'Superior' THEN 3 ELSE 4 END
            """;
        using var reader = command.ExecuteReader();
        var result = new List<Bed>();
        while (reader.Read()) result.Add(new Bed { Id=reader.GetString(0), Room=reader.GetString(1), Code=reader.GetString(2), Level=reader.GetString(3), Locker=reader.GetString(4), Status=reader.GetString(5), PatientId=reader.IsDBNull(6)?null:reader.GetString(6), PatientName=reader.IsDBNull(7)||string.IsNullOrWhiteSpace(reader.GetString(7))?"Sin asignar":reader.GetString(7) });
        return result;
    }

    public void AddPatient(Patient patient, string userName)
    {
        using var connection = Open();
        patient.Folio = string.IsNullOrWhiteSpace(patient.Folio) ? NextFolio(connection) : patient.Folio;
        InsertPatient(connection, null, patient);
        Audit(connection, userName, "Paciente registrado", patient.FullName);
    }

    public (int Patients, int Assignments) ImportLegacyJson(string path, string userName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("patients", out var patients) || patients.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("El archivo no contiene un respaldo válido de SIRASD 0.7.");

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var idMap = new Dictionary<string, string>();
        var imported = 0;
        foreach (var item in patients.EnumerateArray())
        {
            var externalId = GetText(item, "id");
            var patient = new Patient {
                Id = Guid.NewGuid().ToString(), ExternalId = externalId, Folio = GetText(item,"folio"),
                FirstName = GetText(item,"firstName"), LastName = GetText(item,"lastName"), LastName2 = GetText(item,"last2"),
                AdmissionDate = NormalizeDate(GetText(item,"admission")), Status = GetText(item,"status","Activo"),
                TrafficLight = GetText(item,"traffic","Verde"), WeeklyFee = GetDecimal(item,"fee",600m),
                Guardian = GetText(item,"guardian"), Phone = GetText(item,"phone"), Notes = GetText(item,"medical")
            };
            patient.Folio = EnsureUniqueFolio(connection, patient.Folio, transaction);
            if (ExistsPatient(connection, externalId, patient, transaction)) { if(!string.IsNullOrWhiteSpace(externalId)) idMap[externalId]=FindPatientId(connection, externalId, patient, transaction); continue; }
            InsertPatient(connection, transaction, patient); imported++;
            if (!string.IsNullOrWhiteSpace(externalId)) idMap[externalId] = patient.Id;
        }

        var assignments = 0;
        if (root.TryGetProperty("beds", out var beds) && beds.ValueKind == JsonValueKind.Array)
        foreach (var item in beds.EnumerateArray())
        {
            var oldPatientId = GetText(item,"patientId");
            if (string.IsNullOrWhiteSpace(oldPatientId) || !idMap.TryGetValue(oldPatientId, out var patientId)) continue;
            using var update = connection.CreateCommand(); update.Transaction=transaction;
            update.CommandText = "UPDATE Beds SET PatientId=$patient,Status='Ocupada',UpdatedAt=$now WHERE Code=$code AND Level=$level AND (PatientId IS NULL OR PatientId=$patient)";
            update.Parameters.AddWithValue("$patient", patientId); update.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("$code", GetText(item,"code")); update.Parameters.AddWithValue("$level", GetText(item,"level"));
            assignments += update.ExecuteNonQuery();
        }
        ReleaseInactiveBeds(connection, transaction, userName);
        Audit(connection, userName, "Respaldo 0.7 importado", $"{imported} pacientes y {assignments} asignaciones", transaction);
        transaction.Commit();
        return (imported, assignments);
    }

    public void CreateBackup(string destination, string userName)
    {
        using var connection = Open();
        if (string.Equals(Path.GetFullPath(DatabasePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Elige un archivo distinto de la base de datos en uso.");
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination }.ToString());
        backup.Open();
        connection.BackupDatabase(backup);
        Audit(connection, userName, "Respaldo creado", Path.GetFileName(destination));
    }

    private static Patient ReadPatient(SqliteDataReader r) => new() { Id=r.GetString(0), ExternalId=r.IsDBNull(1)?null:r.GetString(1), Folio=r.GetString(2), FirstName=r.GetString(3), LastName=r.GetString(4), LastName2=r.GetString(5), AdmissionDate=r.GetString(6), Status=r.GetString(7), TrafficLight=r.GetString(8), WeeklyFee=(decimal)r.GetDouble(9), Guardian=r.GetString(10), Phone=r.GetString(11), Notes=r.GetString(12) };
    private static void InsertPatient(SqliteConnection c, SqliteTransaction? t, Patient p) { using var q=c.CreateCommand();q.Transaction=t;q.CommandText="INSERT INTO Patients VALUES($id,$external,$folio,$first,$last,$last2,$admission,$status,$traffic,$fee,$guardian,$phone,$notes,$now,$now,NULL)";q.Parameters.AddWithValue("$id",p.Id);q.Parameters.AddWithValue("$external",(object?)p.ExternalId??DBNull.Value);q.Parameters.AddWithValue("$folio",p.Folio);q.Parameters.AddWithValue("$first",p.FirstName);q.Parameters.AddWithValue("$last",p.LastName);q.Parameters.AddWithValue("$last2",p.LastName2);q.Parameters.AddWithValue("$admission",p.AdmissionDate);q.Parameters.AddWithValue("$status",p.Status);q.Parameters.AddWithValue("$traffic",p.TrafficLight);q.Parameters.AddWithValue("$fee",p.WeeklyFee);q.Parameters.AddWithValue("$guardian",p.Guardian);q.Parameters.AddWithValue("$phone",p.Phone);q.Parameters.AddWithValue("$notes",p.Notes);q.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));q.ExecuteNonQuery(); }
    private static bool ExistsPatient(SqliteConnection c,string external,Patient p,SqliteTransaction? t=null){using var q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT COUNT(*) FROM Patients WHERE ($external<>'' AND ExternalId=$external) OR (lower(FirstName)=lower($first) AND lower(LastName)=lower($last) AND lower(LastName2)=lower($last2))";q.Parameters.AddWithValue("$external",external);q.Parameters.AddWithValue("$first",p.FirstName);q.Parameters.AddWithValue("$last",p.LastName);q.Parameters.AddWithValue("$last2",p.LastName2);return Convert.ToInt32(q.ExecuteScalar())>0;}
    private static string FindPatientId(SqliteConnection c,string external,Patient p,SqliteTransaction? t=null){using var q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT Id FROM Patients WHERE ($external<>'' AND ExternalId=$external) OR (lower(FirstName)=lower($first) AND lower(LastName)=lower($last) AND lower(LastName2)=lower($last2)) LIMIT 1";q.Parameters.AddWithValue("$external",external);q.Parameters.AddWithValue("$first",p.FirstName);q.Parameters.AddWithValue("$last",p.LastName);q.Parameters.AddWithValue("$last2",p.LastName2);return Convert.ToString(q.ExecuteScalar())!;}
    private static string NextFolio(SqliteConnection c,SqliteTransaction? t=null){using var q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT Folio FROM Patients";using var r=q.ExecuteReader();var max=0;while(r.Read()){var parts=r.GetString(0).Split('-');if(int.TryParse(parts.LastOrDefault(),out var n))max=Math.Max(max,n);}return $"ASD-{DateTime.Today.Year}-{max+1:000}";}
    private static string EnsureUniqueFolio(SqliteConnection c,string folio,SqliteTransaction? t=null){if(string.IsNullOrWhiteSpace(folio))return NextFolio(c,t);using var q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT COUNT(*) FROM Patients WHERE Folio=$folio";q.Parameters.AddWithValue("$folio",folio);return Convert.ToInt32(q.ExecuteScalar())==0?folio:NextFolio(c,t);}
    private static void Audit(SqliteConnection c,string user,string action,string detail,SqliteTransaction? t=null){using var q=c.CreateCommand();q.Transaction=t;q.CommandText="INSERT INTO AuditLog VALUES($id,$user,$action,$detail,$now)";q.Parameters.AddWithValue("$id",Guid.NewGuid().ToString());q.Parameters.AddWithValue("$user",user);q.Parameters.AddWithValue("$action",action);q.Parameters.AddWithValue("$detail",detail);q.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));q.ExecuteNonQuery();}
    private static string GetText(JsonElement e,string name,string fallback="")=>e.TryGetProperty(name,out var p)&&p.ValueKind!=JsonValueKind.Null?p.ToString():fallback;
    private static decimal GetDecimal(JsonElement e,string name,decimal fallback)=>e.TryGetProperty(name,out var p)&&decimal.TryParse(p.ToString(),NumberStyles.Any,CultureInfo.InvariantCulture,out var value)?value:fallback;
    private static string NormalizeDate(string value)=>DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date.ToString("yyyy-MM-dd"):DateTime.Today.ToString("yyyy-MM-dd");
}
