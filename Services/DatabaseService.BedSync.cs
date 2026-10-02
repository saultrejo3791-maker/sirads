using Microsoft.Data.Sqlite;

namespace SIRASD.Desktop.Services;

public sealed partial class DatabaseService
{
    private static int ReleaseInactiveBeds(SqliteConnection c,SqliteTransaction t,string user,string? patientId=null)
    {
        using var select=CenterCommand(c,t,"""
            SELECT b.Id,b.PatientId,b.Status,p.Status FROM Beds b JOIN Patients p ON p.Id=b.PatientId
            WHERE (lower(trim(p.Status)) IN ('baja','egresado') OR p.DeletedAt IS NOT NULL)
              AND ($patient IS NULL OR p.Id=$patient)
            """,("$patient",patientId));
        var rows=new List<(string Bed,string Patient,string BedStatus,string PatientStatus)>();
        using(var r=select.ExecuteReader())while(r.Read())rows.Add((r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3)));
        foreach(var row in rows)
        {
            using var history=CenterCommand(c,t,"INSERT INTO BedHistory VALUES($id,$bed,$patient,'Liberación por baja / egreso',$reason,$now)",("$id",Guid.NewGuid().ToString()),("$bed",row.Bed),("$patient",row.Patient),("$reason",$"Estado del paciente: {row.PatientStatus}. Registró: {user}"),("$now",Iso(DateTime.Now)));history.ExecuteNonQuery();
            using var update=CenterCommand(c,t,"UPDATE Beds SET PatientId=NULL,Status=CASE WHEN Status='Ocupada' THEN 'Disponible' ELSE Status END,UpdatedAt=$now WHERE Id=$bed",("$bed",row.Bed),("$now",Iso(DateTime.Now)));update.ExecuteNonQuery();
        }
        if(rows.Count>0)Audit(c,user,"Camas sincronizadas con bajas",$"{rows.Count} asignaciones liberadas",t);
        return rows.Count;
    }
    public int SynchronizeInactiveBeds(string user)
    {
        using var c=Open();using var t=c.BeginTransaction(deferred:false);var count=ReleaseInactiveBeds(c,t,user);t.Commit();return count;
    }
}
