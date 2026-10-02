using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SIRASD.Desktop.Models;
using SIRASD.Desktop.Services;

namespace SIRASD.Desktop;

public partial class MedicationsView : UserControl
{
    private DatabaseService _database;
    private string _userName = "";
    private List<Medication> _allMedications = new();
    private List<MedicationMovement> _allMovements = new();
    public ObservableCollection<Medication> Medications { get; } = new();
    public ObservableCollection<MedicationMovement> Movements { get; } = new();

    public MedicationsView() : this(App.Database) { }
    public MedicationsView(DatabaseService database)
    {
        _database = database;
        InitializeComponent();
        DataContext = this;
        SelectTab(false);
    }

    private void Charts_Click(object sender, RoutedEventArgs e) => InventoryCharts.Open(Window.GetWindow(this), _database, true);
    public void SetDatabase(DatabaseService database)
    {
        _database=database;_userName="";_allMedications.Clear();_allMovements.Clear();Medications.Clear();Movements.Clear();
        InventorySearch.Clear();HistorySearch.Clear();StockFilter.SelectedIndex=0;MovementFilter.SelectedIndex=0;FromDate.SelectedDate=null;ToDate.SelectedDate=null;FeedbackText.Text="";
        LotsText.Text="0";LowStockText.Text="0";ExpiryText.Text="0";SelectTab(false);
    }
    public void LoadData(string userName)
    {
        _userName = userName;
        try
        {
            var selectedId = (InventoryGrid.SelectedItem as Medication)?.Id;
            _allMedications = _database.GetMedications();
            _allMovements = _database.GetMedicationMovements();
            LotsText.Text = _allMedications.Count.ToString();
            LowStockText.Text = _allMedications.Count(x => !x.IsExpired && x.IsLowStock).ToString();
            ExpiryText.Text = _allMedications.Count(x => x.Stock > 0 && (x.IsExpired || x.ExpiresSoon)).ToString();
            ApplyFilters();
            InventoryGrid.SelectedItem = Medications.FirstOrDefault(x => x.Id == selectedId);
        }
        catch (Exception ex) { FeedbackText.Text = $"No fue posible cargar los medicamentos: {ex.Message}"; }
    }

    private void ApplyFilters()
    {
        if (InventorySearch is null || HistorySearch is null || StockFilter is null || MovementFilter is null || FromDate is null || ToDate is null) return;
        var selectedId = (InventoryGrid?.SelectedItem as Medication)?.Id;
        var query = InventorySearch.Text.Trim();
        Medications.Clear();
        foreach (var medication in _allMedications.Where(x =>
            ($"{x.DisplayName} {x.Location}".Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
            (StockFilter.SelectedIndex switch { 1 => x.IsLowStock && !x.IsExpired, 2 => x.Stock > 0 && x.ExpiresSoon, 3 => x.IsExpired, _ => true }))) Medications.Add(medication);
        if (InventoryGrid is not null) InventoryGrid.SelectedItem = Medications.FirstOrDefault(x => x.Id == selectedId);
        var historyQuery = HistorySearch.Text.Trim();
        Movements.Clear();
        foreach (var movement in _allMovements.Where(x =>
            $"{x.MedicationName} {x.Lot} {x.PatientDisplay} {x.Responsible} {x.RecordedBy} {x.Reference} {x.Notes}".Contains(historyQuery, StringComparison.CurrentCultureIgnoreCase) &&
            (MovementFilter.SelectedIndex switch { 1 => x.Type == MedicationMovementTypes.Supply, 2 => x.Type is MedicationMovementTypes.Receipt or MedicationMovementTypes.Opening, 3 => x.Type == MedicationMovementTypes.Loss, _ => true }) &&
            (!FromDate.SelectedDate.HasValue || x.OccurredAt.Date >= FromDate.SelectedDate.Value.Date) &&
            (!ToDate.SelectedDate.HasValue || x.OccurredAt.Date <= ToDate.SelectedDate.Value.Date))) Movements.Add(movement);
    }

    private void SelectTab(bool history)
    {
        InventoryPanel.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        HistoryPanel.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        Selector.SetIsSelected(InventoryTab, !history);
        Selector.SetIsSelected(HistoryTab, history);
    }
    private void InventoryTab_Click(object sender, RoutedEventArgs e) => SelectTab(false);
    private void HistoryTab_Click(object sender, RoutedEventArgs e) => SelectTab(true);
    private void Refresh_Click(object sender, RoutedEventArgs e) { FeedbackText.Text = ""; LoadData(_userName); }
    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilters();
    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    { HistorySearch.Clear(); MovementFilter.SelectedIndex = 0; FromDate.SelectedDate = null; ToDate.SelectedDate = null; }
    private void Inventory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReceiveButton is null) return;
        var medication = InventoryGrid.SelectedItem as Medication;
        ReceiveButton.IsEnabled = medication != null;
        LossButton.IsEnabled = medication?.Stock > 0;
        SupplyButton.IsEnabled = medication is { Stock: > 0, IsExpired: false };
        SelectedMedicationText.Text = medication is null ? "Selecciona un lote para registrar un movimiento." : $"{medication.DisplayName} · Disponible: {medication.StockDisplay}";
    }
    private void NewMedication_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MedicationDialog(_database, _userName) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        FeedbackText.Text = "Medicamento registrado. La existencia inicial quedó en la bitácora cuando corresponde.";
        LoadData(_userName);
    }
    private void Receive_Click(object sender, RoutedEventArgs e) => OpenMovement(MedicationMovementTypes.Receipt);
    private void Supply_Click(object sender, RoutedEventArgs e) => OpenMovement(MedicationMovementTypes.Supply);
    private void Loss_Click(object sender, RoutedEventArgs e) => OpenMovement(MedicationMovementTypes.Loss);
    private void OpenMovement(string type)
    {
        if (InventoryGrid.SelectedItem is not Medication medication) return;
        try
        {
            var patients = type == MedicationMovementTypes.Supply ? _database.GetPatients().Where(x => x.Status == "Activo").ToList() : new List<Patient>();
            if (type == MedicationMovementTypes.Supply && patients.Count == 0) { FeedbackText.Text = "Primero registra un paciente activo para vincular el suministro."; return; }
            var dialog = new MedicationMovementDialog(_database, medication, type, patients, _userName) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            FeedbackText.Text = $"{type} registrado. Existencia y bitácora actualizadas.";
            LoadData(_userName);
        }
        catch (Exception ex) { FeedbackText.Text = $"No fue posible registrar el movimiento: {ex.Message}"; }
    }
    private void Detail_Click(object sender, RoutedEventArgs e) => ShowDetail();
    private void History_DoubleClick(object sender, MouseButtonEventArgs e) => ShowDetail();
    private void ShowDetail()
    {
        if (HistoryGrid.SelectedItem is MedicationMovement movement)
            MessageBox.Show(Window.GetWindow(this), movement.Details, "Detalle del movimiento · SIRASD", MessageBoxButton.OK, MessageBoxImage.Information);
        else FeedbackText.Text = "Selecciona un movimiento para consultar su detalle.";
    }
}
