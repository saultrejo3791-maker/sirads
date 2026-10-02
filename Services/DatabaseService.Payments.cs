using Microsoft.Data.Sqlite;
using SIRASD.Desktop.Models;

namespace SIRASD.Desktop.Services;

public sealed partial class DatabaseService
{
    private static readonly string[] PaymentMethods = ["Efectivo", "Transferencia", "Depósito", "Tarjeta", "Otro"];

    private static void InitializePayments(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Payments(
              FolioNumber INTEGER PRIMARY KEY AUTOINCREMENT,
              Id TEXT NOT NULL UNIQUE,
              Folio TEXT NOT NULL UNIQUE DEFAULT '',
              PaidAt TEXT NOT NULL,
              PatientId TEXT NOT NULL REFERENCES Patients(Id),
              PatientFolio TEXT NOT NULL,
              PatientName TEXT NOT NULL,
              Concept TEXT NOT NULL,
              Period TEXT NOT NULL DEFAULT '',
              AmountCents INTEGER NOT NULL CHECK(AmountCents>0 AND AmountCents<=99999999900),
              PaymentMethod TEXT NOT NULL CHECK(PaymentMethod IN ('Efectivo','Transferencia','Depósito','Tarjeta','Otro')),
              PreviousBalanceCents INTEGER NOT NULL CHECK(PreviousBalanceCents BETWEEN -99999999900 AND 99999999900),
              RemainingBalanceCents INTEGER NOT NULL CHECK(RemainingBalanceCents BETWEEN -99999999900 AND 99999999900),
              ReceivedBy TEXT NOT NULL,
              RegisteredBy TEXT NOT NULL,
              CenterName TEXT NOT NULL,
              CenterAddress TEXT NOT NULL DEFAULT '',
              CenterPhone TEXT NOT NULL DEFAULT '',
              CenterRfc TEXT NOT NULL DEFAULT '',
              ReceiptFooter TEXT NOT NULL DEFAULT '',
              CreatedAt TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_Payments_PaidAt ON Payments(PaidAt DESC);
            CREATE INDEX IF NOT EXISTS IX_Payments_Patient ON Payments(PatientId,PaidAt DESC);
            """;
        command.ExecuteNonQuery();
    }

    public Payment SavePayment(PaymentDraft draft, string user)
    {
        RequireText(user, "usuario que registra");
        RequireText(draft.PatientId, "paciente / usuario");
        RequireText(draft.Concept, "concepto de pago");
        RequireText(draft.Period, "periodo o semana que cubre");
        RequireText(draft.PaymentMethod, "forma de pago");
        RequireText(draft.ReceivedBy, "nombre de quien recibe");
        if (draft.PaidAt.Year < 2000 || draft.PaidAt > DateTime.Now.AddMinutes(5)) throw new InvalidOperationException("La fecha y hora del pago no son válidas.");
        if (draft.Concept.Trim().Length > 200) throw new InvalidOperationException("El concepto admite hasta 200 caracteres.");
        if (draft.Period.Trim().Length > 160) throw new InvalidOperationException("El periodo admite hasta 160 caracteres.");
        if (draft.ReceivedBy.Trim().Length > 120) throw new InvalidOperationException("El nombre de quien recibe admite hasta 120 caracteres.");
        if (!PaymentMethods.Contains(draft.PaymentMethod)) throw new InvalidOperationException("Selecciona una forma de pago válida.");
        var amount = PaymentCents(draft.Amount, "monto", true);
        var previous = PaymentCents(draft.PreviousBalance, "saldo anterior");
        var remaining = PaymentCents(draft.RemainingBalance, "saldo restante");
        var now = DateTime.Now;

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        string patientFolio;
        string patientName;
        using (var patient = CenterCommand(connection, transaction, "SELECT Folio,TRIM(FirstName||' '||LastName||' '||LastName2) FROM Patients WHERE Id=$id AND DeletedAt IS NULL", ("$id", draft.PatientId)))
        using (var reader = patient.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("Selecciona un paciente existente.");
            patientFolio = reader.GetString(0);
            patientName = reader.GetString(1);
        }

        var center = ReadCenterReceiptInfo(connection, transaction);
        var id = Guid.NewGuid().ToString();
        using (var insert = CenterCommand(connection, transaction, """
            INSERT INTO Payments(Id,PaidAt,PatientId,PatientFolio,PatientName,Concept,Period,AmountCents,PaymentMethod,
              PreviousBalanceCents,RemainingBalanceCents,ReceivedBy,RegisteredBy,CenterName,CenterAddress,CenterPhone,CenterRfc,ReceiptFooter,CreatedAt)
            VALUES($id,$paid,$patient,$patientFolio,$patientName,$concept,$period,$amount,$method,$previous,$remaining,$received,$registered,$center,$address,$phone,$rfc,$footer,$created)
            """, ("$id", id), ("$paid", Iso(draft.PaidAt)), ("$patient", draft.PatientId), ("$patientFolio", patientFolio), ("$patientName", patientName),
            ("$concept", draft.Concept.Trim()), ("$period", draft.Period.Trim()), ("$amount", amount), ("$method", draft.PaymentMethod),
            ("$previous", previous), ("$remaining", remaining), ("$received", draft.ReceivedBy.Trim()), ("$registered", user.Trim()),
            ("$center", center.Name), ("$address", center.Address), ("$phone", center.Phone), ("$rfc", center.Rfc), ("$footer", center.Footer), ("$created", Iso(now))))
            insert.ExecuteNonQuery();

        long folioNumber;
        using (var number = CenterCommand(connection, transaction, "SELECT last_insert_rowid()")) folioNumber = Convert.ToInt64(number.ExecuteScalar());
        var folio = $"PAG-{folioNumber:000000}";
        using (var update = CenterCommand(connection, transaction, "UPDATE Payments SET Folio=$folio WHERE FolioNumber=$number AND Id=$id", ("$folio", folio), ("$number", folioNumber), ("$id", id)))
            if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("No fue posible asignar el folio al pago.");
        Audit(connection, user, "Pago guardado", $"{folio}; {patientFolio}; {draft.Amount:0.00} MXN", transaction);
        transaction.Commit();

        return new Payment
        {
            Id = id, FolioNumber = folioNumber, Folio = folio, PaidAt = draft.PaidAt, PatientId = draft.PatientId,
            PatientFolio = patientFolio, PatientName = patientName, Concept = draft.Concept.Trim(), Period = draft.Period.Trim(),
            Amount = amount / 100m, PaymentMethod = draft.PaymentMethod, PreviousBalance = previous / 100m,
            RemainingBalance = remaining / 100m, ReceivedBy = draft.ReceivedBy.Trim(), RegisteredBy = user.Trim(),
            CenterName = center.Name, CenterAddress = center.Address, CenterPhone = center.Phone, CenterRfc = center.Rfc,
            ReceiptFooter = center.Footer, CreatedAt = now
        };
    }

    public List<Payment> GetPayments(int limit = 500)
    {
        limit = Math.Clamp(limit, 1, 5000);
        using var connection = Open();
        using var command = CenterCommand(connection, null, PaymentSelect + " ORDER BY FolioNumber DESC LIMIT $limit", ("$limit", limit));
        using var reader = command.ExecuteReader();
        var payments = new List<Payment>();
        while (reader.Read()) payments.Add(ReadPayment(reader));
        return payments;
    }

    public Payment? GetLastPayment()
    {
        using var connection = Open();
        using var command = CenterCommand(connection, null, PaymentSelect + " ORDER BY FolioNumber DESC LIMIT 1");
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadPayment(reader) : null;
    }

    public void RecordPaymentPrint(string paymentId, string user, string paper, bool reprint)
    {
        RequireText(paymentId, "pago"); RequireText(user, "usuario"); RequireText(paper, "formato de impresión");
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: false);
        using var payment = CenterCommand(connection, transaction, "SELECT Folio FROM Payments WHERE Id=$id", ("$id", paymentId));
        var folio = payment.ExecuteScalar() as string ?? throw new InvalidOperationException("El comprobante ya no está disponible.");
        Audit(connection, user, reprint ? "Comprobante reimpreso" : "Comprobante impreso", $"{folio}; {paper}", transaction);
        transaction.Commit();
    }

    private const string PaymentSelect = """
        SELECT Id,FolioNumber,Folio,PaidAt,PatientId,PatientFolio,PatientName,Concept,Period,AmountCents,PaymentMethod,
          PreviousBalanceCents,RemainingBalanceCents,ReceivedBy,RegisteredBy,CenterName,CenterAddress,CenterPhone,CenterRfc,ReceiptFooter,CreatedAt
        FROM Payments
        """;

    private static Payment ReadPayment(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0), FolioNumber = reader.GetInt64(1), Folio = reader.GetString(2), PaidAt = Local(reader.GetString(3)),
        PatientId = reader.GetString(4), PatientFolio = reader.GetString(5), PatientName = reader.GetString(6), Concept = reader.GetString(7),
        Period = reader.GetString(8), Amount = reader.GetInt64(9) / 100m, PaymentMethod = reader.GetString(10),
        PreviousBalance = reader.GetInt64(11) / 100m, RemainingBalance = reader.GetInt64(12) / 100m,
        ReceivedBy = reader.GetString(13), RegisteredBy = reader.GetString(14), CenterName = reader.GetString(15),
        CenterAddress = reader.GetString(16), CenterPhone = reader.GetString(17), CenterRfc = reader.GetString(18),
        ReceiptFooter = reader.GetString(19), CreatedAt = Local(reader.GetString(20))
    };

    private static long PaymentCents(decimal value, string label, bool positive = false)
    {
        if ((positive && value <= 0) || value < -999999999m || value > 999999999m || decimal.Round(value, 2) != value)
            throw new InvalidOperationException($"Revisa {label}: usa hasta dos decimales y un valor dentro del límite permitido.");
        return checked((long)(value * 100m));
    }

    private static CenterReceiptInfo ReadCenterReceiptInfo(SqliteConnection connection, SqliteTransaction? transaction)
    {
        string Value(string key, string fallback = "")
        {
            using var command = CenterCommand(connection, transaction, "SELECT Value FROM AppMeta WHERE Key=$key", ("$key", key));
            return command.ExecuteScalar() as string ?? fallback;
        }
        return new(Value("CenterName", "A Solas con Dios A.C."), Value("CenterAddress"), Value("CenterPhone"), Value("CenterRfc"), Value("ReceiptFooter", "Gracias por su pago."));
    }
}
