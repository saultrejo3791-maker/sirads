using System.Globalization;
using System.Text.Json;

namespace SIRASD.Desktop.Models;

public sealed record RecordField(string Key, string Label, bool Required = false, string Type = "text", string[]? Options = null);
public sealed record RecordModule(string Key, string Title, string Description, bool PatientRequired, RecordField[] Fields);

public static class CenterModules
{
    public static readonly RecordModule[] All =
    [
        new("Incidencias", "Incidencias", "Hechos, acciones y seguimiento de incidencias.", false,
        [new("type","Tipo de incidencia",true),new("description","Descripción de los hechos",true,"memo"),new("actions","Acciones realizadas",true,"memo"),new("followup","Seguimiento / acuerdos",false,"memo"),new("status","Estado",true,"choice",["Abierta","En seguimiento","Cerrada"])]),
        new("Calendario", "Calendario y pagos", "Selecciona un día para consultar eventos y vencimientos de pago.", false,
        [new("type","Tipo",true,"choice",["Evento","Fecha de pago","Cita","Actividad"]),new("place","Lugar / modalidad"),new("amount","Monto previsto (MXN; para fecha de pago)",false,"money"),new("status","Estado",true,"choice",["Pendiente","Realizado","Pagado","Cancelado"]),new("notes","Descripción / referencia de pago",false,"memo")]),
        new("Consejeria", "Consejería de adicciones", "Sesiones, acuerdos y seguimiento por paciente.", true,
        [new("type","Modalidad",true,"choice",["Individual","Grupal","Familiar"]),new("reason","Motivo / temas trabajados",true,"memo"),new("observations","Observaciones de la sesión",true,"memo"),new("agreements","Acuerdos y seguimiento",false,"memo"),new("next","Próxima sesión",false,"date")]),
        new("Historias", "Historias clínicas", "Antecedentes y valoración documentados por el profesional responsable.", true,
        [new("reason","Motivo de atención",true,"memo"),new("background","Antecedentes personales y familiares",false,"memo"),new("allergies","Alergias referidas",false,"memo"),new("current","Padecimiento actual",true,"memo"),new("assessment","Valoración / diagnóstico registrado",false,"memo"),new("plan","Plan y seguimiento",false,"memo"),new("license","Cédula del profesional")]),
        new("Examenes", "Exámenes clínicos", "Solicitudes, resultados y seguimiento de estudios por paciente.", true,
        [new("study","Estudio / examen",true),new("provider","Laboratorio / profesional"),new("status","Estado",true,"choice",["Solicitado","Realizado","Resultado recibido","Cancelado"]),new("results","Resultados y unidades reportadas",false,"memo"),new("interpretation","Interpretación del profesional",false,"memo"),new("followup","Seguimiento",false,"memo")]),
        new("Psiquiatria", "Tratamiento psiquiátrico", "Consultas e indicaciones registradas por el profesional tratante.", true,
        [new("doctor","Psiquiatra tratante",true),new("license","Cédula profesional"),new("assessment","Valoración / diagnóstico registrado",true,"memo"),new("treatment","Tratamiento indicado: medicamento, dosis, vía y frecuencia",true,"memo"),new("evolution","Evolución / efectos observados",false,"memo"),new("next","Próxima consulta",false,"date")]),
        new("Traslados", "Solicitudes de traslado", "Fecha y hora programadas, dirección, contacto y seguimiento de cada solicitud.", false,
        [new("person","Nombre de la persona por ingresar",true),new("contact","Persona de contacto",true),new("phone","Teléfono de contacto",true),new("address","Dirección completa de recogida",true,"memo"),new("references","Referencias del domicilio",false,"memo"),new("destination","Destino",true),new("team","Equipo / vehículo asignado"),new("status","Estado",true,"choice",["Solicitado","Programado","En camino","Completado","Cancelado"]),new("actual","Fecha y hora real del traslado",false,"datetime"),new("notes","Observaciones",false,"memo")])
    ];
    public static RecordModule Get(string key) => All.Single(m => m.Key == key);
}

public sealed class CenterRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Kind { get; set; } = "";
    public string? PatientId { get; set; }
    public string PatientName { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.Now;
    public string Responsible { get; set; } = "";
    public Dictionary<string,string> Fields { get; set; } = new();
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public string Status => Fields.GetValueOrDefault("status", "Registrado");
    public string Type => Fields.GetValueOrDefault("type", "");
    public string Amount => Fields.TryGetValue("amount", out var s) && decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n.ToString("C2", CultureInfo.GetCultureInfo("es-MX")) : "";
    public string Address => Fields.GetValueOrDefault("address", "");
    public CenterRecord Copy() => JsonSerializer.Deserialize<CenterRecord>(JsonSerializer.Serialize(this))!;
}

public sealed class PantryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Alimentos";
    public string Unit { get; set; } = "pieza";
    public string Location { get; set; } = "";
    public decimal Stock { get; set; }
    public decimal Minimum { get; set; }
    public int Version { get; set; }
    public string Status => Stock <= 0 ? "Agotado" : Stock <= Minimum ? "Existencia baja" : "Disponible";
}
public sealed record PantryMovement(string Id, string Item, string Category, string Type, decimal Quantity, decimal Balance, string Unit, string Reference, string Responsible, DateTime OccurredAt, string RecordedBy, string ItemId = "");
