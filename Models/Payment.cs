using System.Globalization;

namespace SIRASD.Desktop.Models;

public sealed class PaymentDraft
{
    public DateTime PaidAt { get; set; } = DateTime.Now;
    public string PatientId { get; set; } = "";
    public string Concept { get; set; } = "";
    public string Period { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "Efectivo";
    public decimal PreviousBalance { get; set; }
    public decimal RemainingBalance { get; set; }
    public string ReceivedBy { get; set; } = "";
}

public sealed class Payment
{
    public string Id { get; set; } = "";
    public long FolioNumber { get; set; }
    public string Folio { get; set; } = "";
    public DateTime PaidAt { get; set; }
    public string PatientId { get; set; } = "";
    public string PatientFolio { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string Concept { get; set; } = "";
    public string Period { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "";
    public decimal PreviousBalance { get; set; }
    public decimal RemainingBalance { get; set; }
    public string ReceivedBy { get; set; } = "";
    public string RegisteredBy { get; set; } = "";
    public string CenterName { get; set; } = "";
    public string CenterAddress { get; set; } = "";
    public string CenterPhone { get; set; } = "";
    public string CenterRfc { get; set; } = "";
    public string ReceiptFooter { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public string PaidAtDisplay => PaidAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    public string AmountDisplay => Amount.ToString("C2", new CultureInfo("es-MX"));
    public string RemainingBalanceDisplay => RemainingBalance.ToString("C2", new CultureInfo("es-MX"));
    public string PatientDisplay => string.IsNullOrWhiteSpace(PatientFolio) ? PatientName : $"{PatientFolio} · {PatientName}";
    public string Details => $"Folio: {Folio}\nFecha y hora: {PaidAtDisplay}\nUsuario: {PatientDisplay}\nConcepto: {Concept}\nPeriodo o semana: {Period}\nMonto: {AmountDisplay}\nForma de pago: {PaymentMethod}\nSaldo anterior: {PreviousBalance.ToString("C2", new CultureInfo("es-MX"))}\nSaldo restante: {RemainingBalanceDisplay}\nRecibió: {ReceivedBy}\nRegistró: {RegisteredBy}";
}
