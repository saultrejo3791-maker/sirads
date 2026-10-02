using System.Globalization;
using System.Windows;
using SIRASD.Desktop.Models;
using SIRASD.Desktop.Services;

namespace SIRASD.Desktop;

public partial class MedicationDialog : Window
{
    private readonly DatabaseService _database;
    private readonly string _userName;
    private readonly Medication _medication = new();
    public MedicationDialog(DatabaseService database, string userName)
    {
        _database = database; _userName = userName;
        InitializeComponent();
        UnitBox.ItemsSource = new[] { "tableta", "cápsula", "mL", "ampolleta", "frasco", "sobre", "pieza" };
        UnitBox.SelectedIndex = 0;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = "";
        if (!MedicationInput.TryQuantity(OpeningBox.Text, out var opening) || !MedicationInput.TryQuantity(MinimumBox.Text, out var minimum))
        { ValidationText.Text = "Escribe cantidades válidas con punto decimal y sin separadores de miles."; return; }
        if (ExpiryPicker.SelectedDate is not DateTime expiry) { ValidationText.Text = "Selecciona la fecha de caducidad."; return; }
        _medication.Name = NameBox.Text; _medication.Strength = StrengthBox.Text; _medication.Presentation = PresentationBox.Text;
        _medication.Unit = UnitBox.SelectedItem as string ?? ""; _medication.Lot = LotBox.Text;
        _medication.ExpiryDate = expiry; _medication.MinimumStock = minimum; _medication.Location = LocationBox.Text;
        SaveButton.IsEnabled = false;
        try { _database.AddMedication(_medication, opening, ReferenceBox.Text, _userName); DialogResult = true; }
        catch (Exception ex) { ValidationText.Text = ex.Message; SaveButton.IsEnabled = true; }
    }
}

internal static class MedicationInput
{
    public static bool TryQuantity(string text, out decimal value) => decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}
