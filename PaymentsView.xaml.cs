using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SIRASD.Desktop.Models;
using SIRASD.Desktop.Services;

namespace SIRASD.Desktop;

public partial class PaymentsView : UserControl
{
    private static readonly CultureInfo Mexico = new("es-MX");
    private DatabaseService _database;
    private string _userName = "";
    private List<Payment> _allPayments = new();
    private List<Patient> _patients = new();
    private Payment? _lastPayment;
    private bool _updatingBalance;
    public ObservableCollection<Payment> Payments { get; } = new();

    public PaymentsView() : this(App.Database) { }
    public PaymentsView(DatabaseService database)
    {
        _database = database;
        InitializeComponent();
        DataContext = this;
    }

    public void SetDatabase(DatabaseService database)
    {
        _database = database; _userName = ""; _allPayments.Clear(); _patients.Clear(); Payments.Clear(); _lastPayment = null;
        PatientBox.ItemsSource = null; PatientBox.SelectedItem = null; SearchBox.Clear(); AmountBox.Clear(); PreviousBalanceBox.Clear(); RemainingBalanceBox.Clear();
        PeriodBox.Clear(); ReceivedByBox.Clear(); LastFolioText.Text = "Sin pagos"; TodayCountText.Text = "0"; TodayTotalText.Text = 0m.ToString("C2", Mexico);
        PrintButton.IsEnabled = false; PreviewButton.IsEnabled = false; ReprintButton.IsEnabled = false; FeedbackText.Text = "";
    }

    public void LoadData(string userName)
    {
        _userName = userName;
        try
        {
            var selectedId = (PaymentsGrid.SelectedItem as Payment)?.Id;
            _patients = _database.GetPatients().OrderBy(x => x.FullName).ToList();
            PatientBox.ItemsSource = _patients;
            _allPayments = _database.GetPayments();
            _lastPayment = _allPayments.FirstOrDefault();
            ApplyFilter();
            PaymentsGrid.SelectedItem = Payments.FirstOrDefault(x => x.Id == selectedId);
            LastFolioText.Text = _lastPayment?.Folio ?? "Sin pagos";
            var today = _allPayments.Where(x => x.PaidAt.Date == DateTime.Today).ToList();
            TodayCountText.Text = today.Count.ToString(CultureInfo.InvariantCulture);
            TodayTotalText.Text = today.Sum(x => x.Amount).ToString("C2", Mexico);
            ReprintButton.IsEnabled = _lastPayment != null;
            if (string.IsNullOrWhiteSpace(ReceivedByBox.Text)) ReceivedByBox.Text = userName;
            if (string.IsNullOrWhiteSpace(PeriodBox.Text)) PeriodBox.Text = CurrentWeek();
            UpdatePrintButtons();
        }
        catch (Exception ex) { FeedbackText.Text = "No fue posible cargar los pagos: " + ex.Message; }
    }

    private void PatientBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PatientBox.SelectedItem is not Patient patient) return;
        AmountBox.Text = patient.WeeklyFee.ToString("0.00", CultureInfo.InvariantCulture);
        PreviousBalanceBox.Text = patient.WeeklyFee.ToString("0.00", CultureInfo.InvariantCulture);
        PeriodBox.Text = CurrentWeek();
        UpdateRemainingBalance();
    }

    private void BalanceInput_Changed(object sender, TextChangedEventArgs e) => UpdateRemainingBalance();
    private void UpdateRemainingBalance()
    {
        if (_updatingBalance || AmountBox is null || PreviousBalanceBox is null || RemainingBalanceBox is null) return;
        if (!TryMoney(AmountBox.Text, out var amount) || !TryMoney(PreviousBalanceBox.Text, out var previous)) return;
        _updatingBalance = true; RemainingBalanceBox.Text = (previous - amount).ToString("0.00", CultureInfo.InvariantCulture); _updatingBalance = false;
    }

    private void SavePayment_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PatientBox.SelectedItem is not Patient patient) throw new InvalidOperationException("Selecciona el paciente o usuario que realiza el pago.");
            var concept = ConceptBox.Text.Trim();
            var amount = ReadMoney(AmountBox.Text, "monto");
            var previous = ReadMoney(PreviousBalanceBox.Text, "saldo anterior");
            var remaining = ReadMoney(RemainingBalanceBox.Text, "saldo restante");
            var method = (PaymentMethodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var confirmation = $"Se guardará un pago de {amount.ToString("C2", Mexico)} para {patient.FullName}.\n\nEl registro recibirá un folio consecutivo y no se podrá editar. ¿Deseas continuar?";
            if (MessageBox.Show(Window.GetWindow(this), confirmation, "Confirmar pago", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var payment = _database.SavePayment(new PaymentDraft
            {
                PaidAt = DateTime.Now, PatientId = patient.Id, Concept = concept, Period = PeriodBox.Text.Trim(), Amount = amount,
                PaymentMethod = method, PreviousBalance = previous, RemainingBalance = remaining, ReceivedBy = ReceivedByBox.Text.Trim()
            }, _userName);
            FeedbackText.Text = $"Pago guardado correctamente con folio {payment.Folio}. Ya puedes imprimir el comprobante.";
            LoadData(_userName); PaymentsGrid.SelectedItem = Payments.FirstOrDefault(x => x.Id == payment.Id); PaymentsGrid.ScrollIntoView(PaymentsGrid.SelectedItem);
        }
        catch (Exception ex) { FeedbackText.Text = "No fue posible guardar el pago: " + ex.Message; }
    }

    private void PrintTicket_Click(object sender, RoutedEventArgs e)
    {
        var payment = PaymentsGrid.SelectedItem as Payment ?? _lastPayment;
        if (payment == null) { FeedbackText.Text = "Primero guarda o selecciona un comprobante."; return; }
        PrintPayment(payment, false);
    }

    private void ReprintLast_Click(object sender, RoutedEventArgs e)
    {
        var payment = _database.GetLastPayment();
        if (payment == null) { FeedbackText.Text = "Todavía no hay un comprobante para reimprimir."; return; }
        PrintPayment(payment, true);
    }

    private void PrintPayment(Payment payment, bool reprint)
    {
        try
        {
            var paper = SelectedPaper();
            if (!ReceiptPrintService.Print(Window.GetWindow(this), payment, paper, reprint)) { FeedbackText.Text = "Impresión cancelada; el comprobante continúa guardado."; return; }
            _database.RecordPaymentPrint(payment.Id, _userName, ReceiptPrintService.PaperName(paper), reprint);
            FeedbackText.Text = reprint ? $"Reimpresión enviada: {payment.Folio}." : $"Comprobante enviado a impresión: {payment.Folio}.";
        }
        catch (Exception ex) { FeedbackText.Text = "No fue posible imprimir el comprobante: " + ex.Message; }
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => ShowPreview();
    private void PaymentsGrid_DoubleClick(object sender, MouseButtonEventArgs e) => ShowPreview();
    private void ShowPreview()
    {
        var payment = PaymentsGrid.SelectedItem as Payment ?? _lastPayment;
        if (payment == null) { FeedbackText.Text = "Primero guarda o selecciona un comprobante."; return; }
        ReceiptPrintService.ShowPreview(Window.GetWindow(this), payment, SelectedPaper());
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void ApplyFilter()
    {
        if (SearchBox is null) return;
        var selectedId = (PaymentsGrid?.SelectedItem as Payment)?.Id;
        var query = SearchBox.Text.Trim(); Payments.Clear();
        foreach (var payment in _allPayments.Where(x => $"{x.Folio} {x.PatientDisplay} {x.Concept} {x.Period} {x.PaymentMethod} {x.ReceivedBy}".Contains(query, StringComparison.CurrentCultureIgnoreCase))) Payments.Add(payment);
        if (PaymentsGrid is not null) PaymentsGrid.SelectedItem = Payments.FirstOrDefault(x => x.Id == selectedId);
    }

    private void PaymentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedPaymentText is null) return;
        var payment = PaymentsGrid.SelectedItem as Payment;
        SelectedPaymentText.Text = payment == null ? "Aún no hay un comprobante seleccionado." : $"{payment.Folio} · {payment.PatientDisplay} · {payment.AmountDisplay} · Saldo restante {payment.RemainingBalanceDisplay}";
        UpdatePrintButtons();
    }

    private void UpdatePrintButtons()
    {
        if (PrintButton is null) return;
        var available = PaymentsGrid.SelectedItem is Payment || _lastPayment != null;
        PrintButton.IsEnabled = available; PreviewButton.IsEnabled = available; ReprintButton.IsEnabled = _lastPayment != null;
    }

    private ReceiptPaper SelectedPaper() => (PaperBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
    {
        "58" => ReceiptPaper.Thermal58,
        "Letter" => ReceiptPaper.Letter,
        _ => ReceiptPaper.Thermal80
    };

    private static decimal ReadMoney(string value, string label)
    {
        if (!TryMoney(value, out var number)) throw new InvalidOperationException($"Escribe un valor válido para {label}, con hasta dos decimales.");
        return number;
    }

    private static bool TryMoney(string value, out decimal number) =>
        decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out number) ||
        decimal.TryParse(value.Trim(), NumberStyles.Currency, Mexico, out number);

    private static string CurrentWeek()
    {
        var today = DateTime.Today; var offset = ((int)today.DayOfWeek + 6) % 7; var monday = today.AddDays(-offset); var sunday = monday.AddDays(6);
        return $"Semana del {monday:dd/MM/yyyy} al {sunday:dd/MM/yyyy}";
    }
}
