using System.Windows;
using SIRASD.Desktop.Models;

namespace SIRASD.Desktop;

public partial class PatientDialog : Window
{
    public Patient Patient { get; private set; } = new();
    public PatientDialog() { InitializeComponent(); AdmissionDate.SelectedDate = DateTime.Today; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FirstName.Text)) { ValidationText.Text="Escribe el nombre del paciente."; return; }
        Patient = new Patient { FirstName=FirstName.Text.Trim(),LastName=LastName.Text.Trim(),LastName2=LastName2.Text.Trim(),AdmissionDate=(AdmissionDate.SelectedDate??DateTime.Today).ToString("yyyy-MM-dd"),Guardian=Guardian.Text.Trim(),Phone=Phone.Text.Trim(),Notes=Notes.Text.Trim() };
        DialogResult = true;
    }
}
