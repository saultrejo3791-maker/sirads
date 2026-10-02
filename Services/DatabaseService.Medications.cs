using System.Globalization;
using Microsoft.Data.Sqlite;
using SIRASD.Desktop.Models;

namespace SIRASD.Desktop.Services;

public sealed partial class DatabaseService
{
    private const long MaximumMedicationUnits = 999_999_999_000;

    private static void InitializeMedications(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Medications(
                Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Strength TEXT NOT NULL,
                Presentation TEXT NOT NULL, Unit TEXT NOT NULL, Lot TEXT NOT NULL,
                ExpiryDate TEXT NOT NULL, StockUnits INTEGER NOT NULL DEFAULT 0 CHECK(StockUnits BETWEEN 0 AND 999999999000),
                MinimumUnits INTEGER NOT NULL DEFAULT 0 CHECK(MinimumUnits BETWEEN 0 AND 999999999000),
                Location TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL,
                UNIQUE(Name,Strength,Presentation,Unit,Lot,ExpiryDate));
            CREATE TABLE IF NOT EXISTS MedicationMovements(
                Id TEXT PRIMARY KEY, MedicationId TEXT NOT NULL REFERENCES Medications(Id),
                Type TEXT NOT NULL CHECK(Type IN ('Entrada inicial','Entrada','Suministro','Baja')),
                QuantityUnits INTEGER NOT NULL CHECK(QuantityUnits > 0),
                BalanceUnits INTEGER NOT NULL CHECK(BalanceUnits >= 0),
                PatientId TEXT REFERENCES Patients(Id), PatientName TEXT NOT NULL DEFAULT '',
                PatientFolio TEXT NOT NULL DEFAULT '', OccurredAt TEXT NOT NULL,
                RecordedAt TEXT NOT NULL, Responsible TEXT NOT NULL, RecordedBy TEXT NOT NULL,
                Reference TEXT NOT NULL DEFAULT '', Notes TEXT NOT NULL DEFAULT '',
                CHECK(Type <> 'Suministro' OR PatientId IS NOT NULL));
            CREATE INDEX IF NOT EXISTS IX_MedicationMovements_Medication ON MedicationMovements(MedicationId,RecordedAt);
            CREATE INDEX IF NOT EXISTS IX_MedicationMovements_Patient ON MedicationMovements(PatientId,OccurredAt);
            """;
        command.ExecuteNonQuery();
    }

    public List<Medication> GetMedications()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,Strength,Presentation,Unit,Lot,ExpiryDate,StockUnits,MinimumUnits,Location FROM Medications ORDER BY Name COLLATE NOCASE,ExpiryDate,Lot";
        using var reader = command.ExecuteReader();
        var items = new List<Medication>();
        while (reader.Read()) items.Add(ReadMedication(reader));
        return items;
    }

    private static Medication ReadMedication(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0), Name = reader.GetString(1), Strength = reader.GetString(2),
        Presentation = reader.GetString(3), Unit = reader.GetString(4), Lot = reader.GetString(5),
        ExpiryDate = DateTime.ParseExact(reader.GetString(6), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Stock = reader.GetInt64(7) / 1000m, MinimumStock = reader.GetInt64(8) / 1000m, Location = reader.GetString(9)
    };

    public void AddMedication(Medication medication, decimal openingQuantity, string reference, string userName)
    {
        Required(userName, "Se necesita una sesión para registrar medicamentos.");
        Required(medication.Name, "Escribe el nombre del medicamento.");
        Required(medication.Strength, "Escribe la concentración indicada en el envase.");
        Required(medication.Presentation, "Escribe la presentación.");
        Required(medication.Unit, "Selecciona la unidad de control.");
        Required(medication.Lot, "Escribe el lote del envase.");
        if (!Guid.TryParse(medication.Id, out _)) throw new ArgumentException("Identificador de medicamento inválido.");
        var opening = ToMedicationUnits(openingQuantity, true);
        var minimum = ToMedicationUnits(medication.MinimumStock, true);
        if (opening > 0) Required(reference, "Indica el origen o referencia de la existencia inicial.");
        // Existing, expired stock can be inventoried, but cannot be supplied.
        if (medication.ExpiryDate.Date == DateTime.MinValue.Date) throw new ArgumentException("Selecciona la caducidad del envase.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Medications WHERE Id=$id";
        command.Parameters.AddWithValue("$id", medication.Id);
        if (Convert.ToInt32(command.ExecuteScalar()) != 0) { transaction.Commit(); return; }
        command.CommandText = """
            INSERT INTO Medications(Id,Name,Strength,Presentation,Unit,Lot,ExpiryDate,StockUnits,MinimumUnits,Location,CreatedAt)
            VALUES($id,$name,$strength,$presentation,$unit,$lot,$expiry,$stock,$minimum,$location,$now)
            """;
        command.Parameters.AddWithValue("$name", medication.Name.Trim());
        command.Parameters.AddWithValue("$strength", medication.Strength.Trim());
        command.Parameters.AddWithValue("$presentation", medication.Presentation.Trim());
        command.Parameters.AddWithValue("$unit", medication.Unit.Trim());
        command.Parameters.AddWithValue("$lot", medication.Lot.Trim());
        command.Parameters.AddWithValue("$expiry", medication.ExpiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$stock", opening);
        command.Parameters.AddWithValue("$minimum", minimum);
        command.Parameters.AddWithValue("$location", medication.Location.Trim());
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        try { command.ExecuteNonQuery(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        { throw new InvalidOperationException("Ese medicamento y lote ya están registrados. Selecciónalos en el inventario y registra una entrada.", ex); }
        if (opening > 0)
        {
            InsertMedicationMovement(connection, transaction, new MedicationMovementRequest
            {
                MedicationId = medication.Id, Type = MedicationMovementTypes.Opening, Quantity = openingQuantity,
                Responsible = userName, Reference = reference.Trim(), Notes = "Existencia al registrar el lote."
            }, opening, opening, "", "", userName);
        }
        Audit(connection, userName, "Medicamento registrado", medication.DisplayName, transaction);
        transaction.Commit();
    }

    public void RecordMedicationMovement(MedicationMovementRequest request, string userName)
    {
        Required(userName, "Se necesita una sesión para registrar movimientos.");
        Required(request.Responsible, "Escribe quién realizó el movimiento.");
        if (!Guid.TryParse(request.Id, out _)) throw new ArgumentException("Identificador de movimiento inválido.");
        if (request.Type is not (MedicationMovementTypes.Receipt or MedicationMovementTypes.Supply or MedicationMovementTypes.Loss))
            throw new ArgumentException("Tipo de movimiento inválido.");
        var quantity = ToMedicationUnits(request.Quantity, false);
        if (request.OccurredAt > DateTime.Now.AddMinutes(1)) throw new ArgumentException("La fecha del movimiento no puede ser futura.");
        if (request.OccurredAt == DateTime.MinValue) throw new ArgumentException("Selecciona la fecha del movimiento.");
        if (request.Type == MedicationMovementTypes.Supply)
        {
            Required(request.PatientId, "Selecciona el paciente que recibió el medicamento.");
            Required(request.Reference, "Registra la indicación o referencia del suministro.");
        }
        else if (!string.IsNullOrEmpty(request.PatientId)) throw new ArgumentException("Solo un suministro puede vincularse con un paciente.");
        if (request.Type == MedicationMovementTypes.Receipt) Required(request.Reference, "Indica el proveedor, origen o referencia de la entrada.");
        if (request.Type == MedicationMovementTypes.Loss) Required(request.Notes, "Explica el motivo de la baja.");

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT MedicationId,Type,QuantityUnits,RecordedBy FROM MedicationMovements WHERE Id=$id";
        command.Parameters.AddWithValue("$id", request.Id);
        using (var previous = command.ExecuteReader())
        {
            if (previous.Read())
            {
                if (previous.GetString(0) != request.MedicationId || previous.GetString(1) != request.Type || previous.GetInt64(2) != quantity || previous.GetString(3) != userName)
                    throw new InvalidOperationException("Este movimiento ya fue registrado con otros datos.");
                previous.Close(); transaction.Commit(); return;
            }
        }
        command.Parameters.Clear();
        command.CommandText = "SELECT Id,Name,Strength,Presentation,Unit,Lot,ExpiryDate,StockUnits,MinimumUnits,Location FROM Medications WHERE Id=$id";
        command.Parameters.AddWithValue("$id", request.MedicationId);
        Medication medication;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("El medicamento no existe. Actualiza el inventario.");
            medication = ReadMedication(reader);
        }
        var patientName = "";
        var patientFolio = "";
        if (request.Type == MedicationMovementTypes.Supply)
        {
            if (medication.IsExpired || request.OccurredAt.Date > medication.ExpiryDate.Date)
                throw new InvalidOperationException("El lote está caducado. No se puede registrar un suministro; puedes registrar su baja.");
            command.Parameters.Clear();
            command.CommandText = "SELECT FirstName,LastName,LastName2,Folio FROM Patients WHERE Id=$id AND Status='Activo' AND DeletedAt IS NULL";
            command.Parameters.AddWithValue("$id", request.PatientId!);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("Selecciona un paciente activo del centro.");
            patientName = string.Join(" ", new[] { reader.GetString(0), reader.GetString(1), reader.GetString(2) }.Where(x => !string.IsNullOrWhiteSpace(x)));
            patientFolio = reader.GetString(3);
        }
        var delta = request.Type == MedicationMovementTypes.Receipt ? quantity : -quantity;
        var previousBalance = ToMedicationUnits(medication.Stock, true);
        var balance = previousBalance + delta;
        if (balance < 0) throw new InvalidOperationException($"Existencia insuficiente. Disponible: {medication.StockDisplay}.");
        if (balance > MaximumMedicationUnits) throw new InvalidOperationException("La entrada supera la cantidad máxima del inventario.");
        command.Parameters.Clear();
        command.CommandText = "UPDATE Medications SET StockUnits=StockUnits+$delta WHERE Id=$id AND StockUnits+$delta BETWEEN 0 AND $maximum";
        command.Parameters.AddWithValue("$delta", delta);
        command.Parameters.AddWithValue("$id", request.MedicationId);
        command.Parameters.AddWithValue("$maximum", MaximumMedicationUnits);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("La existencia cambió. Actualiza el inventario y vuelve a intentarlo.");
        InsertMedicationMovement(connection, transaction, request, quantity, balance, patientName, patientFolio, userName);
        Audit(connection, userName, $"Medicamentos: {request.Type}", $"{medication.DisplayName}; {request.Quantity:0.###} {medication.Unit}; paciente {patientFolio}; movimiento {request.Id}", transaction);
        transaction.Commit();
    }

    private static void InsertMedicationMovement(SqliteConnection connection, SqliteTransaction transaction,
        MedicationMovementRequest request, long quantity, long balance, string patientName, string patientFolio, string userName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO MedicationMovements(Id,MedicationId,Type,QuantityUnits,BalanceUnits,PatientId,PatientName,PatientFolio,
                OccurredAt,RecordedAt,Responsible,RecordedBy,Reference,Notes)
            VALUES($id,$medication,$type,$quantity,$balance,$patient,$name,$folio,$occurred,$now,$responsible,$user,$reference,$notes)
            """;
        command.Parameters.AddWithValue("$id", request.Id);
        command.Parameters.AddWithValue("$medication", request.MedicationId);
        command.Parameters.AddWithValue("$type", request.Type);
        command.Parameters.AddWithValue("$quantity", quantity);
        command.Parameters.AddWithValue("$balance", balance);
        command.Parameters.AddWithValue("$patient", (object?)request.PatientId ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", patientName);
        command.Parameters.AddWithValue("$folio", patientFolio);
        command.Parameters.AddWithValue("$occurred", request.OccurredAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$responsible", request.Responsible.Trim());
        command.Parameters.AddWithValue("$user", userName);
        command.Parameters.AddWithValue("$reference", request.Reference.Trim());
        command.Parameters.AddWithValue("$notes", request.Notes.Trim());
        command.ExecuteNonQuery();
    }

    public List<MedicationMovement> GetMedicationMovements()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.Id,v.MedicationId,m.Name||' · '||m.Strength||' · '||m.Presentation,m.Lot,m.Unit,v.Type,
                   v.QuantityUnits,v.BalanceUnits,v.PatientName,v.PatientFolio,v.OccurredAt,v.RecordedAt,
                   v.Responsible,v.RecordedBy,v.Reference,v.Notes
            FROM MedicationMovements v JOIN Medications m ON m.Id=v.MedicationId
            ORDER BY v.RecordedAt DESC,v.rowid DESC
            """;
        using var reader = command.ExecuteReader();
        var result = new List<MedicationMovement>();
        while (reader.Read()) result.Add(new MedicationMovement
        {
            Id = reader.GetString(0), MedicationId = reader.GetString(1), MedicationName = reader.GetString(2),
            Lot = reader.GetString(3), Unit = reader.GetString(4), Type = reader.GetString(5),
            Quantity = reader.GetInt64(6) / 1000m, Balance = reader.GetInt64(7) / 1000m,
            PatientName = reader.GetString(8), PatientFolio = reader.GetString(9),
            OccurredAt = DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime(),
            RecordedAt = DateTime.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime(),
            Responsible = reader.GetString(12), RecordedBy = reader.GetString(13), Reference = reader.GetString(14), Notes = reader.GetString(15)
        });
        return result;
    }

    private static long ToMedicationUnits(decimal quantity, bool allowZero)
    {
        if (quantity < 0 || (!allowZero && quantity == 0) || quantity > MaximumMedicationUnits / 1000m || decimal.Round(quantity, 3) != quantity)
            throw new ArgumentException("La cantidad debe ser positiva, con máximo 3 decimales y hasta 999999999 unidades. Las existencias iniciales y mínimas pueden ser cero.");
        return checked((long)(quantity * 1000m));
    }

    private static void Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(message);
    }
}
