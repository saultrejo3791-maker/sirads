namespace SIRASD.Desktop.Models;

public sealed class Bed
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Room { get; set; } = "Dormitorio principal";
    public string Code { get; set; } = "";
    public string Level { get; set; } = "";
    public string Locker { get; set; } = "";
    public string Status { get; set; } = "Disponible";
    public string? PatientId { get; set; }
    public string PatientName { get; set; } = "Sin asignar";
}
