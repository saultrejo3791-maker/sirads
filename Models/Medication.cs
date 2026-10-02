namespace SIRASD.Desktop.Models;

public sealed class Medication
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Strength { get; set; } = "";
    public string Presentation { get; set; } = "";
    public string Unit { get; set; } = "tableta";
    public string Lot { get; set; } = "";
    public DateTime ExpiryDate { get; set; } = DateTime.Today.AddYears(1);
    public decimal Stock { get; set; }
    public decimal MinimumStock { get; set; }
    public string Location { get; set; } = "";
    public string DisplayName => $"{Name} · {Strength} · {Presentation} · Lote {Lot}";
    public string StockDisplay => $"{Stock:0.###} {Unit}";
    public bool IsExpired => ExpiryDate.Date < DateTime.Today;
    public bool ExpiresSoon => !IsExpired && ExpiryDate.Date <= DateTime.Today.AddDays(30);
    public bool IsLowStock => Stock <= MinimumStock;
    public string Status => IsExpired ? "Caducado" : Stock == 0 ? "Sin existencias" : ExpiresSoon ? "Por caducar" : IsLowStock ? "Existencia baja" : "Disponible";
}

public static class MedicationMovementTypes
{
    public const string Opening = "Entrada inicial";
    public const string Receipt = "Entrada";
    public const string Supply = "Suministro";
    public const string Loss = "Baja";
}

public sealed class MedicationMovementRequest
{
    // Reused by the same dialog if a saved operation is retried.
    public string Id { get; } = Guid.NewGuid().ToString();
    public string MedicationId { get; set; } = "";
    public string Type { get; set; } = MedicationMovementTypes.Receipt;
    public decimal Quantity { get; set; }
    public string? PatientId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.Now;
    public string Responsible { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class MedicationMovement
{
    public string Id { get; set; } = "";
    public string MedicationId { get; set; } = "";
    public string MedicationName { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Balance { get; set; }
    public string PatientName { get; set; } = "";
    public string PatientFolio { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public DateTime RecordedAt { get; set; }
    public string Responsible { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Notes { get; set; } = "";
    public string QuantityDisplay => $"{(Type is MedicationMovementTypes.Supply or MedicationMovementTypes.Loss ? "−" : "+")}{Quantity:0.###} {Unit}";
    public string PatientDisplay => string.IsNullOrEmpty(PatientName) ? "—" : $"{PatientFolio} · {PatientName}";
    public string Details => $"{MedicationName}\nLote: {Lot}\nMovimiento: {Type}\nCantidad: {QuantityDisplay}\nExistencia posterior: {Balance:0.###} {Unit}\nPaciente: {PatientDisplay}\nFecha del movimiento: {OccurredAt:dd/MM/yyyy HH:mm}\nResponsable: {Responsible}\nRegistró: {RecordedBy}\nFecha de registro: {RecordedAt:dd/MM/yyyy HH:mm:ss}\nIndicación / referencia: {Reference}\nObservaciones / motivo: {Notes}";
}
