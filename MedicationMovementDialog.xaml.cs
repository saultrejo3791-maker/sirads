using System.Globalization;
using System.Windows;
using SIRASD.Desktop.Models;
using SIRASD.Desktop.Services;

namespace SIRASD.Desktop;

public partial class MedicationMovementDialog : Window
{
    private readonly DatabaseService _database;
    private readonly string _userName;
    private readonly MedicationMovementRequest _request = new();
    public MedicationMovementDialog(DatabaseService database, Medication medication, string type, List<Patient> patients, string userName)
    {
        _database = database; _userName = userName;
        _request.MedicationId = medication.Id; _request.Type = type;
        InitializeComponent();
        HeadingText.Text = type switch { MedicationMovementTypes.Supply => "Registrar suministro", MedicationMovementTypes.Loss => "Registrar baja", _ => "Registrar entrada" };
        Title = HeadingText.Text + " · SIRASD";
        MedicationText.Text = medication.DisplayName;
        StockText.Text = $"Existencia: {medication.StockDisplay} · Caducidad: {medication.ExpiryDate:dd/MM/yyyy}";
        QuantityLabel.Text = $"Cantidad ({medication.Unit}) *";
        ResponsibleBox.Text = userName;
        DateBox.SelectedDate = DateTime.Today; TimeBox.Text = DateTime.Now.ToString("HH:mm");
        PatientFields.Visibility = type == MedicationMovementTypes.Supply ? Visibility.Visible : Visibility.Collapsed;
        PatientBox.ItemsSource = patients.Select(p => new { p.Id, Label = $"{p.Folio} · {p.FullName}" }).ToList();
        ReferenceLabel.Text = type switch { MedicationMovementTypes.Supply => "Indicación registrada / referencia médica *", MedicationMovementTypes.Loss => "Referencia del movimiento", _ => "Proveedor, origen o referencia *" };
        NotesLabel.Text = type == MedicationMovementTypes.Loss ? "Motivo de la baja *" : "Observaciones";
        RecordedByText.Text = $"Este movimiento quedará registrado por: {userName}.";
        SaveButton.Content = HeadingText.Text;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = "";
        if (!MedicationInput.TryQuantity(QuantityBox.Text, out var quantity)) { ValidationText.Text = "Escribe una cantidad con punto decimal, sin separadores de miles."; return; }
        if (DateBox.SelectedDate is not DateTime date || !DateTime.TryParseExact(TimeBox.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        { ValidationText.Text = "Selecciona la fecha y escribe la hora en formato HH:mm (24 horas)."; return; }
        _request.Quantity = quantity; _request.OccurredAt = date.Date.Add(time.TimeOfDay);
        _request.PatientId = _request.Type == MedicationMovementTypes.Supply ? PatientBox.SelectedValue as string : null;
        _request.Responsible = ResponsibleBox.Text; _request.Reference = ReferenceBox.Text; _request.Notes = NotesBox.Text;
        SaveButton.IsEnabled = false;
        try { _database.RecordMedicationMovement(_request, _userName); DialogResult = true; }
        catch (Exception ex) { ValidationText.Text = ex.Message; SaveButton.IsEnabled = true; }
    }
}
