namespace SIRASD.Desktop.Models;

public sealed class Patient
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? ExternalId { get; set; }
    public string Folio { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string LastName2 { get; set; } = "";
    public string FullName => string.Join(" ", new[] { FirstName, LastName, LastName2 }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string DisplayName => string.IsNullOrWhiteSpace(Folio) ? FullName : $"{Folio} · {FullName}";
    public string AdmissionDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public string Status { get; set; } = "Activo";
    public string TrafficLight { get; set; } = "Verde";
    public decimal WeeklyFee { get; set; } = 600m;
    public string Guardian { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";
}
