using Microsoft.Data.Sqlite;

namespace SIRASD.Desktop.Services;
public sealed partial class DatabaseService
{
    private static void EnablePantryLosses(SqliteConnection c)
    {
        using var inspect=CenterCommand(c,null,"SELECT sql FROM sqlite_master WHERE name='PantryMovements'");
        var schema=(string)inspect.ExecuteScalar()!;if(schema.Contains("'Baja'"))return;
        using var t=c.BeginTransaction(deferred:false);
        using var migrate=CenterCommand(c,t,"""
            ALTER TABLE PantryMovements RENAME TO PantryMovementsBeforeLoss;
            CREATE TABLE PantryMovements(
              Id TEXT PRIMARY KEY, ItemId TEXT NOT NULL REFERENCES PantryItems(Id), Type TEXT NOT NULL CHECK(Type IN ('Entrada','Salida','Baja')),
              Quantity INTEGER NOT NULL CHECK(Quantity>0), Balance INTEGER NOT NULL CHECK(Balance>=0),
              Reference TEXT NOT NULL, Responsible TEXT NOT NULL, OccurredAt TEXT NOT NULL, RecordedBy TEXT NOT NULL);
            INSERT INTO PantryMovements SELECT * FROM PantryMovementsBeforeLoss;
            DROP TABLE PantryMovementsBeforeLoss;
            """);migrate.ExecuteNonQuery();t.Commit();
    }
}
