using Microsoft.Data.Sqlite;
using SIRASD.Desktop.Models;

namespace SIRASD.Desktop.Services;

public sealed partial class DatabaseService
{
    public string GetCenterName()
    {
        using var c=Open();using var q=CenterCommand(c,null,"SELECT Value FROM AppMeta WHERE Key='CenterName'");
        return q.ExecuteScalar() as string ?? "A Solas con Dios A.C.";
    }
    public void UpdateCenterName(string name,string user)
    {
        RequireText(name,"nombre del centro");RequireText(user,"usuario");
        if(name.Trim().Length>160)throw new InvalidOperationException("El nombre del centro admite hasta 160 caracteres.");
        using var c=Open();using var t=c.BeginTransaction(deferred:false);
        using var q=CenterCommand(c,t,"INSERT INTO AppMeta(Key,Value) VALUES('CenterName',$name) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value",("$name",name.Trim()));q.ExecuteNonQuery();
        Audit(c,user,"Nombre del centro actualizado",name.Trim(),t);t.Commit();
    }
    public CenterReceiptInfo GetCenterReceiptInfo()
    {
        using var c=Open();return ReadCenterReceiptInfo(c,null);
    }
    public void UpdateCenterReceiptInfo(string address,string phone,string rfc,string footer,string user)
    {
        RequireText(user,"usuario");
        if(address.Trim().Length>240)throw new InvalidOperationException("El domicilio admite hasta 240 caracteres.");
        if(phone.Trim().Length>80)throw new InvalidOperationException("El teléfono admite hasta 80 caracteres.");
        if(rfc.Trim().Length>30)throw new InvalidOperationException("El RFC admite hasta 30 caracteres.");
        if(footer.Trim().Length>200)throw new InvalidOperationException("El mensaje final admite hasta 200 caracteres.");
        using var c=Open();using var t=c.BeginTransaction(deferred:false);
        void Save(string key,string value){using var q=CenterCommand(c,t,"INSERT INTO AppMeta(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value",("$key",key),("$value",value.Trim()));q.ExecuteNonQuery();}
        Save("CenterAddress",address);Save("CenterPhone",phone);Save("CenterRfc",rfc);Save("ReceiptFooter",footer);
        Audit(c,user,"Datos de comprobantes actualizados","Domicilio, teléfono, RFC y mensaje final",t);t.Commit();
    }
    internal bool HasUserEmail(string email)
    {
        using var c=Open();using var q=CenterCommand(c,null,"SELECT COUNT(*) FROM Users WHERE Email=$email",("$email",email));return Convert.ToInt32(q.ExecuteScalar())>0;
    }
    internal void ConfigureNewAccount(string email,string name,string hash,string salt,string center)
    {
        using var c=Open();using var t=c.BeginTransaction(deferred:false);
        using(var guard=CenterCommand(c,t,"SELECT (SELECT COUNT(*) FROM Patients)+(SELECT COUNT(*) FROM CenterRecords)+(SELECT COUNT(*) FROM PantryItems)+(SELECT COUNT(*) FROM Medications)+(SELECT COUNT(*) FROM Payments)"))
            if(Convert.ToInt32(guard.ExecuteScalar())!=0)throw new InvalidOperationException("La cuenta nueva requiere una base vacía.");
        using(var remove=CenterCommand(c,t,"DELETE FROM Users"))remove.ExecuteNonQuery();
        using(var insert=CenterCommand(c,t,"INSERT INTO Users VALUES($id,$name,$email,$hash,$salt,'Dirección',1,$now,$now)",("$id",Guid.NewGuid().ToString()),("$name",name),("$email",email),("$hash",hash),("$salt",salt),("$now",Iso(DateTime.Now))))insert.ExecuteNonQuery();
        using(var meta=CenterCommand(c,t,"INSERT INTO AppMeta(Key,Value) VALUES('CenterName',$center)",("$center",center)))meta.ExecuteNonQuery();
        Audit(c,name,"Cuenta independiente creada",center,t);t.Commit();
    }
}
