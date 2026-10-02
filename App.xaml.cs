using System.Windows;
using System.IO;
using System.Diagnostics;
using System.Threading;
using SIRASD.Desktop.Models;
using SIRASD.Desktop.Services;

namespace SIRASD.Desktop;

public partial class App : Application
{
    public static AccountService Accounts { get; } = new();
    public static DatabaseService Database { get; private set; } = Accounts.LegacyDatabase;
    public static void SelectAccount(AccountSession session) => Database = session.Database;
    public static void ClearAccount() => Database = Accounts.LegacyDatabase;
    private Mutex? _instance;
    private bool _ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--verify-installation")
        {

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = 1;
            try
            {
                // Package verification uses a new temporary database, never the user's database.
                var folder = Path.Combine(Path.GetTempPath(), "SIRASD-package-check", Guid.NewGuid().ToString("N"));
                var accounts = new AccountService(folder); accounts.Initialize(); var database = accounts.LegacyDatabase;
                if (database.GetBeds().Count != 30 || database.GetPatients().Count != 0)
                    throw new InvalidOperationException("La comprobación de la base de prueba falló.");
                _ = database.GetMedications();
                _ = database.GetPantryItems();
                _ = database.GetCenterRecords("Calendario");
                _ = database.GetPayments();
                database.UpdateCenterReceiptInfo("Domicilio de prueba", "477 000 0000", "ASD000000XXX", "Gracias por su pago.", "Usuario de prueba");
                var patient = new Patient { FirstName="Paciente", LastName="de prueba", Folio="ASD-TEST-001", WeeklyFee=600m };
                database.AddPatient(patient,"Usuario de prueba");
                var firstPayment=database.SavePayment(new PaymentDraft{PatientId=patient.Id,Concept="Cuota semanal",Period="Semana de prueba",Amount=600m,PaymentMethod="Efectivo",PreviousBalance=600m,RemainingBalance=0m,ReceivedBy="Usuario de prueba"},"Usuario de prueba");
                var secondPayment=database.SavePayment(new PaymentDraft{PatientId=patient.Id,Concept="Aportación",Period="Periodo de prueba",Amount=100m,PaymentMethod="Transferencia",PreviousBalance=100m,RemainingBalance=0m,ReceivedBy="Usuario de prueba"},"Usuario de prueba");
                if(firstPayment.Folio!="PAG-000001"||secondPayment.Folio!="PAG-000002"||database.GetLastPayment()?.Id!=secondPayment.Id||database.GetPayments().Count!=2)
                    throw new InvalidOperationException("La comprobación de pagos y folios falló.");
                if(ReceiptPrintService.CreateDocument(firstPayment,ReceiptPaper.Thermal58,false).Pages.Count!=1||ReceiptPrintService.CreateDocument(firstPayment,ReceiptPaper.Thermal80,false).Pages.Count!=1||ReceiptPrintService.CreateDocument(firstPayment,ReceiptPaper.Letter,true).Pages.Count!=1)
                    throw new InvalidOperationException("La comprobación de formatos de impresión falló.");
                var paymentsView=new PaymentsView(database);paymentsView.LoadData("Usuario de prueba");
                if(paymentsView.Payments.Count!=2)throw new InvalidOperationException("La comprobación de la pantalla de pagos falló.");
                accounts.CreateIndependentAccount("Centro de prueba", "Usuario de prueba", "prueba@example.org", "Prueba2026!", "Prueba2026!");
                var session = accounts.Authenticate("prueba@example.org", "Prueba2026!") ?? throw new InvalidOperationException("Falló la cuenta de prueba.");
                if (session.Database.GetCenterName() != "Centro de prueba" || session.Database.GetPatients().Count != 0 || session.Database.GetPayments().Count != 0) throw new InvalidOperationException("Falló el espacio independiente.");
                var main = new MainWindow(accounts);
                main.Close();
                File.WriteAllText(e.Args[1], "OK: WPF, recursos, SQLite, pagos, folios, formatos de comprobante y ventana principal. Base de prueba: " + folder);
                exitCode = 0;
            }
            catch (Exception ex) { File.WriteAllText(e.Args[1], ex.ToString()); }
            Shutdown(exitCode);
            return;
        }
        _instance = new Mutex(false, @"Local\SIRASD.Desktop.SingleInstance");
        try { _ownsInstance = _instance.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsInstance = true; }
        if (!_ownsInstance)
        {

            MessageBox.Show("SIRASD ya está abierto. Continúa en la ventana existente.", "SIRASD", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(); return;
        }
        base.OnStartup(e);
        try { Accounts.Initialize(); new MainWindow().Show(); }
        catch (Exception ex)
        {

            MessageBox.Show("No fue posible abrir los datos de SIRASD. Conserva tus archivos y el respaldo antes de intentar una recuperación.\n\n" + ex.Message,
                "SIRASD · No se pudo iniciar", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
