using System.IO;
using System.Net.Mail;
using Microsoft.Data.Sqlite;

namespace SIRASD.Desktop.Services;

public sealed record AccountSession(string Email,string Name,string CenterName,DatabaseService Database);

public sealed class AccountService
{
    private readonly string _root;
    public DatabaseService LegacyDatabase { get; }
    public AccountService(string? root=null)
    {
        _root=Path.GetFullPath(root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SIRASD"));
        LegacyDatabase=new DatabaseService(_root);
    }
    private SqliteConnection OpenCatalog()
    {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(_root,"Accounts.db"),ForeignKeys=true}.ToString());c.Open();return c;
    }
    public void Initialize()
    {
        LegacyDatabase.Initialize();
        using var c=OpenCatalog();using var q=c.CreateCommand();q.CommandText="""
            CREATE TABLE IF NOT EXISTS IndependentAccounts(
              Id TEXT PRIMARY KEY, Email TEXT NOT NULL COLLATE NOCASE UNIQUE,
              PasswordHash TEXT NOT NULL, PasswordSalt TEXT NOT NULL, CreatedAt TEXT NOT NULL);
            """;q.ExecuteNonQuery();
    }
    private string AccountDirectory(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new InvalidOperationException("El identificador de la cuenta no es válido.");
        return Path.Combine(_root,"Accounts",id);
    }
    public void CreateIndependentAccount(string center,string name,string email,string password,string confirmation)
    {
        center=center.Trim();name=name.Trim();email=email.Trim();
        if(center.Length==0||center.Length>160)throw new InvalidOperationException("Escribe un nombre de centro de 1 a 160 caracteres.");
        if(name.Length==0||name.Length>100)throw new InvalidOperationException("Escribe el nombre del usuario (hasta 100 caracteres).");
        if(email.Length>254||!MailAddress.TryCreate(email,out var address)||!string.Equals(address.Address,email,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Escribe un correo válido.");
        if(password.Length<8||password.Length>128||string.IsNullOrWhiteSpace(password))throw new InvalidOperationException("La contraseña debe tener de 8 a 128 caracteres.");
        if(password!=confirmation)throw new InvalidOperationException("Las contraseñas no coinciden.");
        var (hash,salt)=PasswordService.Hash(password);
        using var c=OpenCatalog();using var transaction=c.BeginTransaction(deferred:false);
        using(var exists=c.CreateCommand())
        {
            exists.Transaction=transaction;exists.CommandText="SELECT COUNT(*) FROM IndependentAccounts WHERE Email=$email";exists.Parameters.AddWithValue("$email",email);
            if(Convert.ToInt32(exists.ExecuteScalar())!=0||LegacyDatabase.HasUserEmail(email))throw new InvalidOperationException("Ese correo ya está registrado. Usa otro correo para la cuenta independiente.");
        }
        var id=Guid.NewGuid().ToString("N");var directory=AccountDirectory(id);
        // The account is listed only after its separate database is fully configured.
        // A failed setup may leave an unlisted empty folder; it never alters existing accounts.
        var database=new DatabaseService(directory);database.Initialize();database.ConfigureNewAccount(email,name,hash,salt,center);
        using var insert=c.CreateCommand();insert.Transaction=transaction;insert.CommandText="INSERT INTO IndependentAccounts VALUES($id,$email,$hash,$salt,$now)";
        insert.Parameters.AddWithValue("$id",id);insert.Parameters.AddWithValue("$email",email);insert.Parameters.AddWithValue("$hash",hash);insert.Parameters.AddWithValue("$salt",salt);insert.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));insert.ExecuteNonQuery();transaction.Commit();
    }
    public AccountSession? Authenticate(string email,string password)
    {
        email=email.Trim();
        string? id=null,hash=null,salt=null;
        using(var c=OpenCatalog())using(var q=c.CreateCommand())
        {
            q.CommandText="SELECT Id,PasswordHash,PasswordSalt FROM IndependentAccounts WHERE Email=$email";q.Parameters.AddWithValue("$email",email);
            using var r=q.ExecuteReader();if(r.Read()){id=r.GetString(0);hash=r.GetString(1);salt=r.GetString(2);}
        }
        if(id==null)
        {
            var legacyName=LegacyDatabase.Authenticate(email,password);
            return legacyName==null?null:new(email,legacyName,LegacyDatabase.GetCenterName(),LegacyDatabase);
        }
        if(!PasswordService.Verify(password,hash!,salt!))return null;
        var directory=AccountDirectory(id);
        if(!File.Exists(Path.Combine(directory,"SIRASD.db")))throw new InvalidOperationException("No se encontró la base de esta cuenta. Conserva el respaldo y solicita su recuperación; no se creará una base vacía sobre tus registros.");
        var db=new DatabaseService(directory);db.Initialize();
        var name=db.Authenticate(email,password);
        if(name==null)throw new InvalidOperationException("La información de acceso no coincide con la base de esta cuenta. Revisa su respaldo.");
        return new(email,name,db.GetCenterName(),db);
    }
}
