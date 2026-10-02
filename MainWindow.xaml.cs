using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SIRASD.Desktop.Models;

namespace SIRASD.Desktop;

public partial class MainWindow : Window
{
    public ObservableCollection<Patient> Patients { get; } = new();
    public ObservableCollection<Bed> Beds { get; } = new();
    private List<Patient> _allPatients = new();
    private string _currentUser = "";

    private readonly SIRASD.Desktop.Services.AccountService _accounts;
    public MainWindow() : this(App.Accounts) { }
    public MainWindow(SIRASD.Desktop.Services.AccountService accounts)
    {
        _accounts=accounts;
        InitializeComponent();
        DataContext = this;
        InitializeCenterNavigation();
        CurrentDate.Text = DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-MX"));
    }

    private void Login_Click(object sender, RoutedEventArgs e) => Login();
    private void PasswordBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Login(); }

    private void Login()
    {
        SIRASD.Desktop.Services.AccountSession? session;
        try { session=_accounts.Authenticate(EmailBox.Text,PasswordBox.Password); }
        catch(Exception ex) { LoginError.Text=ex.Message;return; }
        if(session is null){LoginError.Text="Correo o contraseña incorrectos.";return;}
        ClearSessionViews(); App.SelectAccount(session); MedicationsPanel.SetDatabase(App.Database); PaymentsPanel.SetDatabase(App.Database);
        var name=session.Name; ApplyCenterBranding();
        _currentEmail = EmailBox.Text.Trim(); _currentUser = name; SignedUser.Text = name; LoginError.Text = "";
        WelcomeTitle.Text = $"Bienvenido, {name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name}.";
        LoginView.Visibility = Visibility.Collapsed; ApplicationView.Visibility = Visibility.Visible;
        RefreshAll(); ShowPanel("Inicio");
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        _currentUser = ""; _currentEmail=""; ClearSessionViews(); App.ClearAccount(); MedicationsPanel.SetDatabase(App.Database); PaymentsPanel.SetDatabase(App.Database); ResetCenterBranding(); PasswordBox.Clear(); ApplicationView.Visibility = Visibility.Collapsed; LoginView.Visibility = Visibility.Visible;
    }

    private void Navigation_Click(object sender, RoutedEventArgs e) => ShowPanel((string)((Button)sender).Tag);

    private void ShowPanel(string page)
    {
        DashboardPanel.Visibility = page == "Inicio" ? Visibility.Visible : Visibility.Collapsed;
        PatientsPanel.Visibility = page == "Pacientes" ? Visibility.Visible : Visibility.Collapsed;
        PaymentsPanel.Visibility = page == "Pagos" ? Visibility.Visible : Visibility.Collapsed;
        BedsPanel.Visibility = page == "Camas" ? Visibility.Visible : Visibility.Collapsed;
        BackupsPanel.Visibility = page == "Respaldos" ? Visibility.Visible : Visibility.Collapsed;
        MedicationsPanel.Visibility = page == "Medicamentos" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = page == "Camas" ? "Camas y lockers" : page;
        PageSubtitle.Text = page switch
        {
            "Pacientes" => "Cada persona, su historia y su proceso.",
            "Pagos" => "Registra pagos y entrega comprobantes con folio consecutivo.",
            "Camas" => "Un lugar para cada persona. Todo en orden.",
            "Respaldos" => "Cuida la información que acompaña al centro.",
            "Medicamentos" => "Inventario por lote, suministros y bitácora de uso.",
            _ => "Una mirada al día a día del centro."
        };
        foreach (var button in new[] { HomeNav, PatientsNav, PaymentsNav, BedsNav, MedicationsNav, BackupsNav })
            System.Windows.Controls.Primitives.Selector.SetIsSelected(button, (string)button.Tag == page);
        if (page is "Pacientes" or "Camas") RefreshAll();
        if (page == "Medicamentos") MedicationsPanel.LoadData(_currentUser);
        if (page == "Pagos") PaymentsPanel.LoadData(_currentUser);
        ShowCenterPanel(page);
        if (page == "Inicio") RefreshAll();
    }

    private void RefreshAll()
    {
        _allPatients = App.Database.GetPatients(); ApplyPatientFilter();
        Beds.Clear(); foreach (var bed in App.Database.GetBeds()) Beds.Add(bed);
        var stats = App.Database.GetStats(); ActivePatientsText.Text=stats.ActivePatients.ToString(); OccupiedBedsText.Text=stats.OccupiedBeds.ToString(); AvailableBedsText.Text=stats.AvailableBeds.ToString(); IncidentsText.Text=stats.Incidents.ToString();
        var occupancy = Beds.Count == 0 ? 0 : 100.0 * stats.OccupiedBeds / Beds.Count;
        OccupancyBar.Value = occupancy;
        OccupancyText.Text = $"{occupancy:0}%";
        CapacityText.Text = $"{stats.OccupiedBeds} de {Beds.Count} camas ocupadas";
    }

    private void PatientSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyPatientFilter();
    private void ApplyPatientFilter()
    {
        if (PatientSearch is null) return;
        var query = PatientSearch.Text.Trim(); Patients.Clear();
        foreach (var patient in _allPatients.Where(p => string.IsNullOrWhiteSpace(query) || p.FullName.Contains(query, StringComparison.CurrentCultureIgnoreCase) || p.Folio.Contains(query, StringComparison.OrdinalIgnoreCase))) Patients.Add(patient);
    }

    private void NewPatient_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PatientDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        App.Database.AddPatient(dialog.Patient, _currentUser); StatusText.Text = $"Paciente registrado: {dialog.Patient.FullName}"; RefreshAll();
    }

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title="Seleccionar respaldo de SIRASD 0.7", Filter="Respaldo JSON (*.json)|*.json" };
        if (picker.ShowDialog() != true) return;
        try { var result=App.Database.ImportLegacyJson(picker.FileName,_currentUser);StatusText.Text=$"Importación terminada: {result.Patients} pacientes y {result.Assignments} asignaciones nuevas.";RefreshAll();MessageBox.Show(StatusText.Text,"SIRASD",MessageBoxButton.OK,MessageBoxImage.Information); }
        catch(Exception ex){MessageBox.Show(ex.Message,"No fue posible importar",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Title="Guardar respaldo de SIRASD 0.9", Filter="Base de datos SIRASD (*.sirasd)|*.sirasd", FileName=$"SIRASD_Respaldo_{DateTime.Now:yyyyMMdd_HHmm}.sirasd" };
        if (picker.ShowDialog() != true) return;
        try { App.Database.CreateBackup(picker.FileName,_currentUser);StatusText.Text="Respaldo guardado correctamente.";MessageBox.Show(StatusText.Text,"SIRASD",MessageBoxButton.OK,MessageBoxImage.Information); }
        catch(Exception ex){MessageBox.Show(ex.Message,"No fue posible crear el respaldo",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
}
