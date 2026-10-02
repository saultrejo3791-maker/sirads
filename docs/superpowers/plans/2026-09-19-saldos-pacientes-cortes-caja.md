# Saldos de pacientes y cortes de caja Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Incorporar a SIRASD un saldo auditable por paciente, cargos semanales opcionales cada lunes, pagos y cancelaciones transaccionales, cortes de caja consultables/cerrables y su impresión completa en 58 mm, 80 mm y hoja/PDF.

**Architecture:** Mantener un saldo actual en Patients para lectura rápida y una bitácora PatientBalanceMovements como fuente de auditoría. Los pagos, sus reversiones y los cortes se guardan en transacciones SQLite; la interfaz WPF consume servicios parciales de DatabaseService y conserva los estilos existentes. Las impresiones individuales y de corte usan FixedDocument con medidas térmicas compartidas y paginación explícita.

**Tech Stack:** C# 14, .NET 10 Windows, WPF, Microsoft.Data.Sqlite 10.0.11, FixedDocument/DocumentPaginator, consola de pruebas propia sin paquetes de prueba externos.

**Spec:** docs/superpowers/specs/2026-09-19-saldos-pacientes-cortes-caja-design.md

## Global Constraints

- Mantener SIRASD como aplicación local WPF para Windows con base SQLite separada por cuenta.
- Conservar pacientes, pagos, folios y comprobantes existentes; no reescribir registros históricos.
- Guardar dinero nuevo en centavos enteros y aceptar como máximo dos decimales.
- Usar fecha local del centro para semanas de lunes a domingo y UTC ISO-8601 para auditoría.
- Activar la automatización sin cargo inmediato; el primer cargo corresponde al lunes estrictamente posterior.
- Cobrar únicamente pacientes con Status igual a Activo; Egresado y Baja pausan la programación.
- Bloquear ajustes manuales mientras AutoDebtEnabled sea verdadero.
- Mantener todos los pagos y cancelaciones; ninguna corrección elimina un folio.
- No incluir un movimiento de caja en dos cierres definitivos.
- Conservar los recursos visuales de App.xaml y los patrones actuales de tarjetas, botones y tablas.
- Para 58 mm usar 48 mm de página imprimible y margen interno de 1 mm por lado.
- No afirmar impresión física hasta realizarla con la unidad térmica conectada.
- No incorporar servicios en la nube, facturación fiscal, cobro bancario ni nuevos roles.
- Antes de tocar código, inicializar Git local con la exclusión existente de bases, respaldos, binarios y obj; no subir el repositorio a ningún servicio remoto.

## Review Focus

- Cambio del reloj del equipo hacia adelante o atrás: no duplicar semanas, no aceptar pagos futuros y conservar fechas ya registradas.
- Activación, desactivación y reactivación en lunes: la activación siempre programa el lunes siguiente; la desactivación procesa primero un lunes ya vencido.
- Corte térmico con cientos de folios y textos largos: paginar sin perder, duplicar ni recortar líneas.
- Cancelación posterior a un corte cerrado: mantener el corte original y llevar la reversión al siguiente cierre.
- Migración interrumpida, repetida o con base WAL: crear respaldo SQLite válido, ejecutar una sola vez y dejar datos previos intactos ante error.

---

### Task 0: Línea base segura y repositorio local

**Files:**
- Verify: .gitignore
- Verify: SIRASD.Desktop.csproj
- Verify: App.xaml.cs
- Include in baseline: docs/superpowers/specs/2026-09-19-saldos-pacientes-cortes-caja-design.md
- Include in baseline: docs/superpowers/plans/2026-09-19-saldos-pacientes-cortes-caja.md

**Interfaces:**
- Consumes: código SIRASD 0.9.7 y diseño aprobado.
- Produces: repositorio Git local sin remoto, commit baseline y resultados reproducibles de compilación/verificador.

- [ ] **Step 1: Confirmar exclusiones de datos sensibles y generados**

Run:

~~~powershell
Get-Content .gitignore
Get-ChildItem -Recurse -File -Include *.db,*.db-wal,*.db-shm,*.sirasd
~~~

Expected: .gitignore contiene bin/, obj/, Entrega_Windows/, *.db, *.db-shm, *.db-wal y *.sirasd; no se prepara ninguna base de usuario para Git.

- [ ] **Step 2: Ejecutar la compilación y verificador actuales antes de cambiar código**

Run:

~~~powershell
dotnet build .\SIRASD.Desktop.csproj -c Release
$verify = Join-Path $env:TEMP "sirasd-baseline-verification.txt"
dotnet run --project .\SIRASD.Desktop.csproj -c Release -- --verify-installation $verify
Get-Content -LiteralPath $verify
~~~

Expected: build exit 0 y archivo iniciado con "OK: WPF, recursos, SQLite, pagos, folios, formatos de comprobante y ventana principal."

- [ ] **Step 3: Inicializar exclusivamente el repositorio local y revisar qué se incluirá**

Run:

~~~powershell
git init
git add .gitignore *.cs *.xaml *.csproj *.md *.txt *.ps1 Assets Installer Models Services docs
git status --short
git remote -v
~~~

Expected: ningún *.db, *.sirasd, bin/ u obj/ aparece; git remote -v no muestra destinos.

- [ ] **Step 4: Crear el punto de retorno inicial**

Run:

~~~powershell
git commit -m "chore: capture SIRASD 0.9.7 baseline"
git status --short
~~~

Expected: commit creado y árbol limpio.

### Task 1: Arnés de pruebas y reglas financieras puras

**Files:**
- Modify: SIRASD.Desktop.csproj
- Create: Properties/AssemblyInfo.cs
- Create: Services/FinancialRules.cs
- Create: tests/SIRASD.FinancialTests/SIRASD.FinancialTests.csproj
- Create: tests/SIRASD.FinancialTests/TestCase.cs
- Create: tests/SIRASD.FinancialTests/TestAssert.cs
- Create: tests/SIRASD.FinancialTests/Program.cs
- Create: tests/SIRASD.FinancialTests/FinancialRulesTests.cs

**Interfaces:**
- Consumes: DateOnly y cantidades long en centavos.
- Produces: FinancialRules.NextMonday(DateOnly), FinancialRules.DueMondays(DateOnly, DateOnly), FinancialRules.FormatBalance(long), FinancialRules.ToCents(decimal, string, bool); arnés que descubre clases ITestSuite automáticamente.

- [ ] **Step 1: Crear el proyecto de pruebas sin dependencias externas**

SIRASD.FinancialTests.csproj:

~~~xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>SIRASD.FinancialTests</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\SIRASD.Desktop.csproj" />
  </ItemGroup>
</Project>
~~~

Add to SIRASD.Desktop.csproj:

~~~xml
<ItemGroup>
  <Compile Remove="tests\**\*.cs" />
  <EmbeddedResource Remove="tests\**\*" />
  <None Remove="tests\**\*" />
</ItemGroup>
~~~

Properties/AssemblyInfo.cs:

~~~csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SIRASD.FinancialTests")]
~~~

- [ ] **Step 2: Crear el corredor de pruebas y aserciones**

TestCase.cs:

~~~csharp
namespace SIRASD.FinancialTests;

internal sealed record TestCase(string Name, Action Body);

internal interface ITestSuite
{
    IEnumerable<TestCase> Cases();
}
~~~

TestAssert.cs:

~~~csharp
namespace SIRASD.FinancialTests;

internal static class TestAssert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }

    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException ex) { return ex; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
~~~

Program.cs:

~~~csharp
using System.Reflection;

namespace SIRASD.FinancialTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var filter = args.Length == 0 ? "" : args[0];
        var suites = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => !t.IsAbstract && typeof(ITestSuite).IsAssignableFrom(t))
            .Select(t => (ITestSuite)Activator.CreateInstance(t)!)
            .SelectMany(s => s.Cases())
            .Where(t => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var failures = new List<string>();
        foreach (var test in suites)
        {
            try { test.Body(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception ex) { failures.Add($"{test.Name}: {ex}"); Console.WriteLine($"FAIL {test.Name}"); }
        }

        if (suites.Count == 0) failures.Add($"No tests matched '{filter}'.");
        if (failures.Count == 0) return 0;
        Console.Error.WriteLine(string.Join(Environment.NewLine + Environment.NewLine, failures));
        return 1;
    }
}
~~~

- [ ] **Step 3: Escribir pruebas fallidas para calendario, dinero y presentación**

FinancialRulesTests.cs debe probar:

~~~csharp
using SIRASD.Desktop.Services;

namespace SIRASD.FinancialTests;

internal sealed class FinancialRulesTests : ITestSuite
{
    public IEnumerable<TestCase> Cases()
    {
        yield return new("rules.next Monday from Saturday", () =>
            TestAssert.Equal(new DateOnly(2026, 9, 21), FinancialRules.NextMonday(new DateOnly(2026, 9, 19))));
        yield return new("rules.activation on Monday waits seven days", () =>
            TestAssert.Equal(new DateOnly(2026, 9, 28), FinancialRules.NextMonday(new DateOnly(2026, 9, 21))));
        yield return new("rules.recovers each due Monday once", () =>
            TestAssert.Equal("2026-09-07,2026-09-14,2026-09-21",
                string.Join(",", FinancialRules.DueMondays(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 21)))));
        yield return new("rules.future schedule has no due Monday", () =>
            TestAssert.Equal(0, FinancialRules.DueMondays(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 21)).Count));
        yield return new("rules.debt display", () =>
            TestAssert.Equal("Deuda: $1,300.00 MXN", FinancialRules.FormatBalance(130000)));
        yield return new("rules.credit display", () =>
            TestAssert.Equal("Saldo a favor: $200.00 MXN", FinancialRules.FormatBalance(-20000)));
        yield return new("rules.zero display", () =>
            TestAssert.Equal("Sin saldo pendiente", FinancialRules.FormatBalance(0)));
        yield return new("rules.rejects fractions below one cent", () =>
            TestAssert.Throws<InvalidOperationException>(() => FinancialRules.ToCents(10.001m, "monto", true)));
    }
}
~~~

- [ ] **Step 4: Ejecutar y confirmar que falla porque FinancialRules aún no existe**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- rules.
~~~

Expected: compile FAIL indicando que FinancialRules no existe.

- [ ] **Step 5: Implementar las reglas puras mínimas**

Services/FinancialRules.cs:

~~~csharp
using System.Collections.ObjectModel;
using System.Globalization;

namespace SIRASD.Desktop.Services;

internal static class FinancialRules
{
    private static readonly CultureInfo Mexico = new("es-MX");
    internal const long MaximumCents = 99_999_999_900;

    internal static DateOnly NextMonday(DateOnly date)
    {
        var days = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        if (days == 0) days = 7;
        return date.AddDays(days);
    }

    internal static IReadOnlyList<DateOnly> DueMondays(DateOnly next, DateOnly today)
    {
        var result = new List<DateOnly>();
        for (var day = next; day <= today; day = day.AddDays(7)) result.Add(day);
        return new ReadOnlyCollection<DateOnly>(result);
    }

    internal static long ToCents(decimal value, string label, bool positive)
    {
        if ((positive && value <= 0) || value < -999_999_999m || value > 999_999_999m || decimal.Round(value, 2) != value)
            throw new InvalidOperationException($"Revisa {label}: usa hasta dos decimales y un valor dentro del límite permitido.");
        return checked((long)(value * 100m));
    }

    internal static string FormatBalance(long cents) => cents switch
    {
        > 0 => $"Deuda: {(cents / 100m).ToString("C2", Mexico)} MXN",
        < 0 => $"Saldo a favor: {(-cents / 100m).ToString("C2", Mexico)} MXN",
        _ => "Sin saldo pendiente"
    };
}
~~~

- [ ] **Step 6: Ejecutar reglas y compilación completa**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- rules.
dotnet build .\SIRASD.Desktop.csproj -c Release
~~~

Expected: todas las pruebas rules pasan y build exit 0.

- [ ] **Step 7: Commit**

~~~powershell
git add SIRASD.Desktop.csproj Properties Services\FinancialRules.cs tests\SIRASD.FinancialTests
git commit -m "test: add financial rules harness"
~~~

### Task 2: Modelos y migración financiera segura

**Files:**
- Modify: Models/Patient.cs
- Modify: Models/Payment.cs
- Create: Models/PatientFinance.cs
- Create: Models/CashCut.cs
- Modify: Services/DatabaseService.cs
- Create: Services/DatabaseService.FinanceSchema.cs
- Create: tests/SIRASD.FinancialTests/DatabaseFixture.cs
- Create: tests/SIRASD.FinancialTests/FinanceMigrationTests.cs

**Interfaces:**
- Consumes: esquema 0.9.7 y DatabaseService.Initialize().
- Produces: Patient con BalanceCents, AutoDebtEnabled, NextAutoChargeDate y LastAutoChargeDate; tablas PatientBalanceMovements, PaymentReversals, CashCuts y CashCutLines; FinanceSchemaVersion=1.

- [ ] **Step 1: Añadir modelos con nombres y tipos definitivos**

Patient.cs añade:

~~~csharp
public long BalanceCents { get; set; }
public bool AutoDebtEnabled { get; set; }
public DateOnly? NextAutoChargeDate { get; set; }
public DateOnly? LastAutoChargeDate { get; set; }
public string BalanceDisplay => Services.FinancialRules.FormatBalance(BalanceCents);
public string AutoDebtDisplay => AutoDebtEnabled ? "Automático" : "Manual";
public bool CanAdjustBalance => !AutoDebtEnabled;
public bool CanToggleAutomaticDebt => Status == "Activo";
~~~

PatientFinance.cs define:

~~~csharp
namespace SIRASD.Desktop.Models;

public static class BalanceMovementTypes
{
    public const string Opening = "Saldo inicial";
    public const string WeeklyCharge = "Cuota semanal";
    public const string AppliedPayment = "Pago aplicado";
    public const string PaymentCancellation = "Cancelación de pago";
    public const string ManualAdjustment = "Ajuste manual";
}

public sealed class PatientBalanceMovement
{
    public string Id { get; set; } = "";
    public string PatientId { get; set; } = "";
    public string Type { get; set; } = "";
    public long DeltaCents { get; set; }
    public long PreviousBalanceCents { get; set; }
    public long ResultingBalanceCents { get; set; }
    public DateOnly? WeekMonday { get; set; }
    public string ReferenceType { get; set; } = "";
    public string ReferenceId { get; set; } = "";
    public string Reason { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedAt { get; set; }
}

public sealed record BalanceAdjustmentRequest(string PatientId, decimal NewBalance, string Reason);
public sealed record WeeklyChargeRunResult(int ChargesCreated, long TotalCents);
~~~

Payment.cs añade AppliesToBalance a PaymentDraft, pero conserva temporalmente PreviousBalance y RemainingBalance para que los llamadores existentes sigan compilando hasta Task 5:

~~~csharp
public bool AppliesToBalance { get; set; } = true;
~~~

Payment añade:

~~~csharp
public bool AppliesToBalance { get; set; }
public string Status { get; set; } = "Vigente";
public bool CashCutEligible { get; set; }
public DateTime? CancelledAt { get; set; }
public string CancelledBy { get; set; } = "";
public string CancellationReason { get; set; } = "";
public string CashCutNumber { get; set; } = "";
public bool IsCancelled => Status == "Cancelado";
~~~

CashCut.cs define los tipos completos que consumen el servicio, la interfaz y la impresión:

~~~csharp
namespace SIRASD.Desktop.Models;

public sealed record CashCutQuery(DateOnly From, DateOnly Through, bool PendingOnly = false)
{
    public DateTime FromInclusive => From.ToDateTime(TimeOnly.MinValue);
    public DateTime ToExclusive => Through.AddDays(1).ToDateTime(TimeOnly.MinValue);
}

public sealed class PaymentReversal
{
    public string Id { get; set; } = "";
    public string PaymentId { get; set; } = "";
    public string OriginalFolio { get; set; } = "";
    public long AmountCents { get; set; }
    public string PaymentMethod { get; set; } = "";
    public bool AppliesToBalance { get; set; }
    public long ResultingBalanceCents { get; set; }
    public string Reason { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedAt { get; set; }
}

public sealed class CashCutLine
{
    public string SourceType { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Folio { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public string PatientName { get; set; } = "";
    public string Concept { get; set; } = "";
    public string Period { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public long AmountCents { get; set; }
    public bool AppliesToBalance { get; set; }
    public long ResultingBalanceCents { get; set; }
    public string Status { get; set; } = "";
    public string CashCutDisplayNumber { get; set; } = "";
}

public sealed class CashCutPreview
{
    public CashCutQuery Query { get; set; } = new(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));
    public IReadOnlyList<CashCutLine> Lines { get; set; } = Array.Empty<CashCutLine>();
    public long CashCents { get; set; }
    public long TransferCents { get; set; }
    public long DepositCents { get; set; }
    public long CardCents { get; set; }
    public long OtherCents { get; set; }
    public long ReversalCents { get; set; }
    public long NetCents { get; set; }
    public int EntryCount => Lines.Count;
}

public sealed class CashCut
{
    public long CutNumber { get; set; }
    public string Id { get; set; } = "";
    public string DisplayNumber { get; set; } = "";
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodThrough { get; set; }
    public DateTime ClosedAt { get; set; }
    public string ClosedBy { get; set; } = "";
    public string Notes { get; set; } = "";
    public long CashCents { get; set; }
    public long TransferCents { get; set; }
    public long DepositCents { get; set; }
    public long CardCents { get; set; }
    public long OtherCents { get; set; }
    public long ReversalCents { get; set; }
    public long NetCents { get; set; }
    public int EntryCount { get; set; }
    public IReadOnlyList<CashCutLine> Lines { get; set; } = Array.Empty<CashCutLine>();
}
~~~

- [ ] **Step 2: Escribir pruebas fallidas de migración**

DatabaseFixture debe crear una carpeta temporal, una base con el esquema anterior, un paciente y dos pagos con saldos restantes 50000 y 30000. FinanceMigrationTests debe verificar:

La clase de apoyo expone estas firmas, que las tareas posteriores reutilizan sin acceder a la base real:

~~~csharp
internal sealed class DatabaseFixture : IDisposable
{
    internal string Root { get; }
    internal DatabaseService Database { get; }
    internal static DatabaseFixture Empty();
    internal static DatabaseFixture LegacyWithTwoPayments();
    internal Patient AddPatient(decimal weeklyFee, string status, long balanceCents = 0);
    internal PaymentDraft Payment(string patientId, decimal amount, bool applies, string method = "Efectivo");
    internal void EnableForNextRun(string patientId, DateOnly nextMonday);
    internal int CountRows(string table, string whereClause = "1=1");
    public void Dispose();
}
~~~

Empty crea una carpeta con Path.Combine(Path.GetTempPath(), "SIRASD-finance-tests", Guid.NewGuid().ToString("N")) e inicializa DatabaseService. AddPatient usa AddPatient y, solo para preparar estados imposibles desde una tarea anterior, actualiza BalanceCents mediante una conexión SQLite directa a Root/SIRASD.db. EnableForNextRun actualiza AutoDebtEnabled=1 y NextAutoChargeDate con formato yyyy-MM-dd. Dispose elimina exclusivamente Root después de verificar que cuelga de SIRASD-finance-tests.

~~~csharp
yield return new("migration.preserves legacy rows and picks latest balance", () =>
{
    using var fixture = DatabaseFixture.LegacyWithTwoPayments();
    fixture.Database.Initialize();
    var patient = fixture.Database.GetPatients().Single();
    TestAssert.Equal(30000L, patient.BalanceCents);
    TestAssert.Equal(false, patient.AutoDebtEnabled);
    TestAssert.Equal("PAG-000002", fixture.Database.GetLastPayment()!.Folio);
    TestAssert.Equal(2, fixture.Database.GetPayments().Count);
});

yield return new("migration.is idempotent", () =>
{
    using var fixture = DatabaseFixture.LegacyWithTwoPayments();
    fixture.Database.Initialize();
    fixture.Database.Initialize();
    TestAssert.Equal(1, fixture.CountRows("PatientBalanceMovements", "Type='Saldo inicial'"));
});

yield return new("migration.creates one valid prefinance backup", () =>
{
    using var fixture = DatabaseFixture.LegacyWithTwoPayments();
    fixture.Database.Initialize();
    var backups = Directory.GetFiles(Path.Combine(fixture.Root, "MigrationBackups"), "*.sirasd");
    TestAssert.Equal(1, backups.Length);
    TestAssert.True(new FileInfo(backups[0]).Length > 0, "Migration backup is empty.");
});
~~~

- [ ] **Step 3: Ejecutar y confirmar fallo por columnas/tablas ausentes**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- migration.
~~~

Expected: FAIL por BalanceCents o FinanceSchemaVersion ausentes.

- [ ] **Step 4: Crear respaldo previo y migración transaccional**

DatabaseService.FinanceSchema.cs debe:

1. Leer AppMeta FinanceSchemaVersion.
2. Si es menor que 1 y DatabasePath ya existía, crear MigrationBackups/SIRASD-before-finance-v1-AAAAMMDD-HHMMSS.sirasd mediante SqliteConnection.BackupDatabase.
3. Iniciar transacción inmediata.
4. Añadir columnas ausentes con PRAGMA table_info.
5. Crear tablas e índices.
6. Migrar el saldo del último pago por PatientId.
7. Marcar pagos existentes CashCutEligible=0.
8. Insertar un movimiento de apertura por paciente.
9. Guardar FinanceSchemaVersion=1 y confirmar.

Las columnas añadidas son exactamente:

- Patients: BalanceCents INTEGER NOT NULL DEFAULT 0, AutoDebtEnabled INTEGER NOT NULL DEFAULT 0, NextAutoChargeDate TEXT NULL, LastAutoChargeDate TEXT NULL y FinancialUpdatedAt TEXT NULL.
- Payments: AppliesToBalance INTEGER NOT NULL DEFAULT 1, Status TEXT NOT NULL DEFAULT 'Vigente', CashCutEligible INTEGER NOT NULL DEFAULT 0, CancelledAt TEXT NULL, CancelledBy TEXT NOT NULL DEFAULT '' y CancellationReason TEXT NOT NULL DEFAULT ''.

DatabaseService.Initialize llama InitializeFinance(connection) inmediatamente después de InitializePayments(connection). Los INSERT de pagos nuevos escriben CashCutEligible=1 de forma explícita; el DEFAULT 0 protege los registros históricos migrados.

SQL nuclear:

~~~sql
CREATE TABLE IF NOT EXISTS PatientBalanceMovements(
  Id TEXT PRIMARY KEY,
  PatientId TEXT NOT NULL REFERENCES Patients(Id),
  Type TEXT NOT NULL,
  DeltaCents INTEGER NOT NULL,
  PreviousBalanceCents INTEGER NOT NULL,
  ResultingBalanceCents INTEGER NOT NULL,
  WeekMonday TEXT,
  ReferenceType TEXT NOT NULL DEFAULT '',
  ReferenceId TEXT NOT NULL DEFAULT '',
  Reason TEXT NOT NULL DEFAULT '',
  RecordedBy TEXT NOT NULL,
  RecordedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_BalanceMovement_Weekly
ON PatientBalanceMovements(PatientId,WeekMonday)
WHERE Type='Cuota semanal';

CREATE TABLE IF NOT EXISTS PaymentReversals(
  Id TEXT PRIMARY KEY,
  PaymentId TEXT NOT NULL UNIQUE REFERENCES Payments(Id),
  OriginalFolio TEXT NOT NULL,
  AmountCents INTEGER NOT NULL CHECK(AmountCents<0),
  PaymentMethod TEXT NOT NULL,
  AppliesToBalance INTEGER NOT NULL,
  ResultingBalanceCents INTEGER NOT NULL,
  Reason TEXT NOT NULL,
  RecordedBy TEXT NOT NULL,
  RecordedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS CashCuts(
  CutNumber INTEGER PRIMARY KEY AUTOINCREMENT,
  Id TEXT NOT NULL UNIQUE,
  DisplayNumber TEXT NOT NULL UNIQUE DEFAULT '',
  PeriodFrom TEXT NOT NULL,
  PeriodThrough TEXT NOT NULL,
  ClosedAt TEXT NOT NULL,
  ClosedBy TEXT NOT NULL,
  Notes TEXT NOT NULL DEFAULT '',
  CashCents INTEGER NOT NULL,
  TransferCents INTEGER NOT NULL,
  DepositCents INTEGER NOT NULL,
  CardCents INTEGER NOT NULL,
  OtherCents INTEGER NOT NULL,
  ReversalCents INTEGER NOT NULL,
  NetCents INTEGER NOT NULL,
  EntryCount INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS CashCutLines(
  Id TEXT PRIMARY KEY,
  CashCutId TEXT NOT NULL REFERENCES CashCuts(Id),
  SourceType TEXT NOT NULL CHECK(SourceType IN ('Pago','Cancelación')),
  SourceId TEXT NOT NULL,
  Folio TEXT NOT NULL,
  OccurredAt TEXT NOT NULL,
  PatientName TEXT NOT NULL,
  Concept TEXT NOT NULL,
  Period TEXT NOT NULL,
  PaymentMethod TEXT NOT NULL,
  AmountCents INTEGER NOT NULL,
  AppliesToBalance INTEGER NOT NULL,
  ResultingBalanceCents INTEGER NOT NULL,
  Status TEXT NOT NULL,
  UNIQUE(SourceType,SourceId)
);
~~~

- [ ] **Step 5: Actualizar lectura e inserción de pacientes y pagos**

DatabaseService.cs debe seleccionar las nuevas columnas por nombre/orden fijo y InsertPatient debe enumerar columnas, no usar INSERT INTO Patients VALUES. PaymentSelect debe incluir columnas nuevas y usar LEFT JOIN a CashCutLines/CashCuts para CashCutNumber.

- [ ] **Step 6: Ejecutar migración, todos los tests y build**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- migration.
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release
dotnet build .\SIRASD.Desktop.csproj -c Release
~~~

Expected: PASS; una segunda Initialize no crea otro movimiento ni respaldo.

- [ ] **Step 7: Commit**

~~~powershell
git add Models Services\DatabaseService.cs Services\DatabaseService.FinanceSchema.cs tests\SIRASD.FinancialTests
git commit -m "feat: add financial schema migration"
~~~

### Task 3: Motor idempotente de cargos de los lunes

**Files:**
- Create: Services/DatabaseService.PatientFinance.cs
- Create: tests/SIRASD.FinancialTests/WeeklyChargeTests.cs

**Interfaces:**
- Consumes: FinancialRules, Patients y PatientBalanceMovements.
- Produces: SetAutomaticDebt(string patientId, bool enabled, DateTime localNow, string user), ApplyPendingWeeklyCharges(DateTime localNow, string user), GetBalanceMovements(string patientId).

- [ ] **Step 1: Escribir pruebas fallidas de calendario y saldo**

WeeklyChargeTests debe cubrir:

~~~csharp
yield return new("weekly.enable Saturday schedules Monday without immediate charge", () =>
{
    using var f = DatabaseFixture.Empty();
    var p = f.AddPatient(500m, "Activo");
    f.Database.SetAutomaticDebt(p.Id, true, new DateTime(2026, 9, 19, 10, 0, 0), "Dirección");
    var current = f.Database.GetPatients().Single();
    TestAssert.Equal(0L, current.BalanceCents);
    TestAssert.Equal(new DateOnly(2026, 9, 21), current.NextAutoChargeDate);
});

yield return new("weekly.enable Monday waits following Monday", () =>
{
    using var f = DatabaseFixture.Empty();
    var p = f.AddPatient(500m, "Activo");
    f.Database.SetAutomaticDebt(p.Id, true, new DateTime(2026, 9, 21, 8, 0, 0), "Dirección");
    TestAssert.Equal(new DateOnly(2026, 9, 28), f.Database.GetPatients().Single().NextAutoChargeDate);
});

yield return new("weekly.reopening creates missed Mondays once", () =>
{
    using var f = DatabaseFixture.Empty();
    var p = f.AddPatient(500m, "Activo");
    f.Database.SetAutomaticDebt(p.Id, true, new DateTime(2026, 9, 1), "Dirección");
    var first = f.Database.ApplyPendingWeeklyCharges(new DateTime(2026, 9, 21, 9, 0, 0), "Sistema");
    var second = f.Database.ApplyPendingWeeklyCharges(new DateTime(2026, 9, 21, 18, 0, 0), "Sistema");
    TestAssert.Equal(3, first.ChargesCreated);
    TestAssert.Equal(0, second.ChargesCreated);
    TestAssert.Equal(150000L, f.Database.GetPatients().Single().BalanceCents);
});

yield return new("weekly.credit is consumed by charge", () =>
{
    using var f = DatabaseFixture.Empty();
    var p = f.AddPatient(500m, "Activo", balanceCents: -20000);
    f.EnableForNextRun(p.Id, new DateOnly(2026, 9, 21));
    f.Database.ApplyPendingWeeklyCharges(new DateTime(2026, 9, 21), "Sistema");
    TestAssert.Equal(30000L, f.Database.GetPatients().Single().BalanceCents);
});
~~~

- [ ] **Step 2: Ejecutar y verificar fallo por métodos ausentes**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- weekly.
~~~

Expected: compile FAIL por SetAutomaticDebt o ApplyPendingWeeklyCharges.

- [ ] **Step 3: Implementar cargo semanal dentro de una transacción inmediata**

El bucle central debe usar saldo y cuota leídos dentro de la misma transacción:

~~~csharp
foreach (var monday in FinancialRules.DueMondays(nextDate, today))
{
    var feeCents = FinancialRules.ToCents(weeklyFee, "cuota semanal", false);
    if (feeCents == 0)
    {
        nextDate = monday.AddDays(7);
        continue;
    }
    var movementId = Guid.NewGuid().ToString();
    var newBalance = checked(balance + feeCents);
    using var insert = CenterCommand(connection, transaction, """
        INSERT OR IGNORE INTO PatientBalanceMovements
          (Id,PatientId,Type,DeltaCents,PreviousBalanceCents,ResultingBalanceCents,WeekMonday,
           ReferenceType,ReferenceId,Reason,RecordedBy,RecordedAt)
        VALUES($id,$patient,'Cuota semanal',$delta,$before,$after,$week,'Semana',$week,
               'Cargo automático semanal',$user,$now)
        """,
        ("$id", movementId), ("$patient", patientId), ("$delta", feeCents),
        ("$before", balance), ("$after", newBalance), ("$week", monday.ToString("yyyy-MM-dd")),
        ("$user", user), ("$now", Iso(localNow)));

    if (insert.ExecuteNonQuery() == 1) balance = newBalance;
    nextDate = monday.AddDays(7);
}
~~~

Después actualizar Patients solo con el saldo realmente insertado, LastAutoChargeDate y NextAutoChargeDate. La restricción única es la segunda defensa contra duplicados.

- [ ] **Step 4: Probar reloj hacia atrás y límite monetario**

Añadir casos:

- ejecutar con fecha anterior a NextAutoChargeDate produce cero cargos;
- cuota cero crea movimiento cero solo si la política admite cuota cero; para este plan no crea movimiento y avanza la semana;
- saldo más cuota que rebasa MaximumCents rechaza toda la transacción;
- una fecha futura almacenada no se corrige retroactivamente ni duplica cargos.

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- weekly.
~~~

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add Services\DatabaseService.PatientFinance.cs tests\SIRASD.FinancialTests\WeeklyChargeTests.cs
git commit -m "feat: add idempotent weekly charges"
~~~

### Task 4: Ajustes manuales y transiciones de estado del paciente

**Files:**
- Modify: Services/DatabaseService.PatientFinance.cs
- Modify: Services/DatabaseService.Center.cs
- Create: tests/SIRASD.FinancialTests/PatientFinanceTests.cs

**Interfaces:**
- Consumes: SetAutomaticDebt y helper transaccional de cargos.
- Produces: AdjustPatientBalance(BalanceAdjustmentRequest request, DateTime localNow, string user), UpdatePatient con pausa/reactivación coherente.

- [ ] **Step 1: Escribir pruebas fallidas de bloqueo, auditoría y estados**

Casos exactos:

~~~csharp
yield return new("patient.adjustment is blocked while automatic", () =>
{
    using var f = DatabaseFixture.Empty();
    var p = f.AddPatient(500m, "Activo");
    f.Database.SetAutomaticDebt(p.Id, true, new DateTime(2026, 9, 19), "Dirección");
    TestAssert.Throws<InvalidOperationException>(() =>
        f.Database.AdjustPatientBalance(new(p.Id, 900m, "Corrección"), new DateTime(2026, 9, 19), "Dirección"));
});

yield return new("patient.manual adjustment stores delta and reason", () =>
{
    using var f = DatabaseFixture.Empty();
    var p = f.AddPatient(500m, "Activo", balanceCents: 30000);
    f.Database.AdjustPatientBalance(new(p.Id, 100m, "Convenio actualizado"), new DateTime(2026, 9, 19), "Dirección");
    var movement = f.Database.GetBalanceMovements(p.Id).Single(x => x.Type == "Ajuste manual");
    TestAssert.Equal(-20000L, movement.DeltaCents);
    TestAssert.Equal("Convenio actualizado", movement.Reason);
});

yield return new("patient.inactive pauses and reactivation waits next Monday", () =>
{
    using var f = DatabaseFixture.Empty();
    var original = f.AddPatient(500m, "Activo");
    f.Database.SetAutomaticDebt(original.Id, true, new DateTime(2026, 9, 1), "Dirección");
    var inactive = f.Database.GetPatients().Single();
    inactive.Status = "Baja";
    f.Database.UpdatePatient(inactive, f.Database.GetPatients().Single(), "Dirección", new DateTime(2026, 9, 8));
    var paused = f.Database.GetPatients().Single();
    TestAssert.Equal(null, paused.NextAutoChargeDate);
    paused.Status = "Activo";
    f.Database.UpdatePatient(paused, f.Database.GetPatients().Single(), "Dirección", new DateTime(2026, 9, 10));
    TestAssert.Equal(new DateOnly(2026, 9, 14), f.Database.GetPatients().Single().NextAutoChargeDate);
});
~~~

- [ ] **Step 2: Ejecutar y verificar fallo**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- patient.
~~~

Expected: FAIL por ajuste ausente o firma UpdatePatient sin fecha determinista.

- [ ] **Step 3: Implementar ajuste con razón y una sola transacción**

Firma:

~~~csharp
public Patient AdjustPatientBalance(
    BalanceAdjustmentRequest request,
    DateTime localNow,
    string user)
~~~

Validar usuario, paciente, motivo de 3 a 500 caracteres, dos decimales y modo manual. Calcular delta como newBalanceCents - oldBalanceCents, actualizar Patients e insertar PatientBalanceMovements con Type ManualAdjustment. Rechazar delta cero con "El nuevo saldo es igual al saldo actual."

- [ ] **Step 4: Integrar estado con cargos vencidos**

Cambiar la firma a:

~~~csharp
public void UpdatePatient(Patient patient, Patient original, string user, DateTime? localNow = null)
~~~

Si pasa de Activo a Egresado/Baja, ejecutar primero el helper de cargos vencidos en la misma conexión/transacción y dejar NextAutoChargeDate en NULL. Si pasa de Egresado/Baja a Activo y AutoDebtEnabled es verdadero, programar FinancialRules.NextMonday(DateOnly.FromDateTime(now)). No cobrar semanas inactivas.

- [ ] **Step 5: Ejecutar pruebas financieras y regresión de pacientes**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- patient.
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release
dotnet build .\SIRASD.Desktop.csproj -c Release
~~~

Expected: PASS.

- [ ] **Step 6: Commit**

~~~powershell
git add Services\DatabaseService.PatientFinance.cs Services\DatabaseService.Center.cs tests\SIRASD.FinancialTests\PatientFinanceTests.cs
git commit -m "feat: audit patient balance adjustments"
~~~

### Task 5: Pagos transaccionales y cancelaciones inmutables

**Files:**
- Modify: Models/Payment.cs
- Modify: Services/DatabaseService.Payments.cs
- Modify: PaymentsView.xaml.cs
- Modify: App.xaml.cs
- Create: tests/SIRASD.FinancialTests/PaymentFinanceTests.cs

**Interfaces:**
- Consumes: Patient BalanceCents y PatientBalanceMovements.
- Produces: SavePayment(PaymentDraft, string, DateTime? localNow = null) calculando saldos; CancelPayment(string paymentId, string reason, DateTime localNow, string user); GetPaymentReversals.

- [ ] **Step 1: Escribir pruebas fallidas para pago aplicado, no aplicado y excedente**

~~~csharp
yield return new("payment.applied reduces total debt beyond weekly fee", () =>
{
    using var f = DatabaseFixture.Empty();
    var p = f.AddPatient(500m, "Activo", balanceCents: 200000);
    var payment = f.Database.SavePayment(f.Payment(p.Id, 700m, applies: true), "Caja");
    TestAssert.Equal(200000m / 100m, payment.PreviousBalance);
    TestAssert.Equal(130000m / 100m, payment.RemainingBalance);
    TestAssert.Equal(130000L, f.Database.GetPatients().Single().BalanceCents);
});

yield return new("payment.overpayment creates credit", () =>
{
    using var f = DatabaseFixture.Empty();
    var p = f.AddPatient(500m, "Activo", balanceCents: 50000);
    var payment = f.Database.SavePayment(f.Payment(p.Id, 700m, applies: true), "Caja");
    TestAssert.Equal(-200m, payment.RemainingBalance);
});

yield return new("payment.not applied leaves balance unchanged", () =>
{
    using var f = DatabaseFixture.Empty();
    var p = f.AddPatient(500m, "Activo", balanceCents: 50000);
    var payment = f.Database.SavePayment(f.Payment(p.Id, 100m, applies: false), "Caja");
    TestAssert.Equal(500m, payment.RemainingBalance);
    TestAssert.Equal(0, f.Database.GetBalanceMovements(p.Id).Count(x => x.Type == "Pago aplicado"));
});
~~~

- [ ] **Step 2: Escribir pruebas fallidas de cancelación**

Cubrir pago aplicado, no aplicado, doble cancelación, motivo vacío y pago inexistente:

~~~csharp
yield return new("payment.cancel restores balance and creates cash reversal", () =>
{
    using var f = DatabaseFixture.Empty();
    var p = f.AddPatient(500m, "Activo", balanceCents: 50000);
    var payment = f.Database.SavePayment(f.Payment(p.Id, 700m, applies: true), "Caja");
    var reversal = f.Database.CancelPayment(payment.Id, "Captura duplicada", new DateTime(2026, 9, 19), "Dirección");
    TestAssert.Equal(-70000L, reversal.AmountCents);
    TestAssert.Equal(50000L, f.Database.GetPatients().Single().BalanceCents);
    TestAssert.Equal("Cancelado", f.Database.GetPayments().Single().Status);
});
~~~

- [ ] **Step 3: Ejecutar y verificar fallo**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- payment.
~~~

Expected: FAIL porque SavePayment aún confía en saldos enviados por la UI y CancelPayment no existe.

- [ ] **Step 4: Hacer que SavePayment derive ambos saldos dentro de la transacción**

Dentro de SavePayment:

~~~csharp
using var patient = CenterCommand(connection, transaction, """
    SELECT Folio,TRIM(FirstName||' '||LastName||' '||LastName2),BalanceCents
    FROM Patients WHERE Id=$id AND DeletedAt IS NULL
    """, ("$id", draft.PatientId));
~~~

Calcular:

~~~csharp
var previous = patientBalanceCents;
var remaining = draft.AppliesToBalance ? checked(previous - amount) : previous;
~~~

La firma usa DateTime? localNow = null y resuelve var now = localNow ?? DateTime.Now. La interfaz usa el valor predeterminado; las pruebas pasan una fecha fija para validar pagos futuros y auditoría sin depender del reloj del equipo.

Insertar AppliesToBalance, Status Vigente y CashCutEligible=1. Si aplica, actualizar Patients e insertar PatientBalanceMovements antes del commit. Eliminar PreviousBalance/RemainingBalance de PaymentDraft para que ninguna pantalla pueda imponerlos.

En el mismo paso, actualizar PaymentsView.xaml.cs para dejar de asignar PreviousBalance/RemainingBalance al construir PaymentDraft y enviar AppliesToBalance=true hasta que Task 7 agregue la casilla visible. Actualizar también los dos PaymentDraft del verificador en App.xaml.cs; el primero debe preparar una deuda mediante AdjustPatientBalance y aplicarse, y el segundo debe usar AppliesToBalance=false. Así cada commit continúa compilando y el verificador conserva una expectativa financiera explícita.

- [ ] **Step 5: Implementar CancelPayment como reversión, nunca DELETE**

Dentro de una transacción inmediata:

1. Leer el pago y confirmar Status Vigente.
2. Insertar PaymentReversals con AmountCents negativo.
3. Actualizar Payments a Cancelado con motivo/usuario/fecha.
4. Si AppliesToBalance, sumar AmountCents al saldo e insertar movimiento PaymentCancellation.
5. Auditar y confirmar.

El INSERT en PaymentReversals tiene PaymentId UNIQUE para bloquear la segunda cancelación incluso con dos ventanas.

- [ ] **Step 6: Probar fecha futura y rollback**

Añadir pruebas que:

- PaidAt mayor a localNow + 5 minutos falla sin consumir folio;
- overflow del saldo revierte pago y movimiento;
- doble cancelación conserva una sola reversión;
- cancelar un pago no aplicado no cambia BalanceCents.

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- payment.
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release
~~~

Expected: PASS.

- [ ] **Step 7: Commit**

~~~powershell
git add Models\Payment.cs Services\DatabaseService.Payments.cs PaymentsView.xaml.cs App.xaml.cs tests\SIRASD.FinancialTests\PaymentFinanceTests.cs
git commit -m "feat: make payments and cancellations transactional"
~~~

### Task 6: Saldo y automatización en la lista de pacientes

**Files:**
- Modify: MainWindow.xaml
- Modify: MainWindow.xaml.cs
- Modify: MainWindow.Center.cs
- Modify: CenterForms.cs
- Create: BalanceAdjustmentDialog.xaml
- Create: BalanceAdjustmentDialog.xaml.cs
- Create: BalanceHistoryDialog.xaml
- Create: BalanceHistoryDialog.xaml.cs
- Create: tests/SIRASD.FinancialTests/WpfTestHost.cs
- Create: tests/SIRASD.FinancialTests/PatientUiSmokeTests.cs

**Interfaces:**
- Consumes: SetAutomaticDebt, AdjustPatientBalance y GetBalanceMovements.
- Produces: columnas Saldo/Automático/Ajustar, diálogo común de ajuste e historial de movimientos.

- [ ] **Step 1: Escribir smoke test fallido de columnas y bloqueo**

PatientUiSmokeTests debe iniciar recursos WPF una sola vez, construir MainWindow con AccountService temporal y verificar:

~~~csharp
var headers = window.PatientsGrid.Columns.Select(c => c.Header?.ToString()).ToList();
TestAssert.True(headers.Contains("Saldo"), "Missing patient balance column.");
TestAssert.True(headers.Contains("Automático"), "Missing automatic debt column.");
TestAssert.True(headers.Contains("Ajustar"), "Missing balance action column.");
~~~

También crear BalanceAdjustmentDialog con AutoDebtEnabled=true y verificar que su botón de guardar está deshabilitado.

WpfTestHost.cs:

~~~csharp
using System.Windows;
using SIRASD.Desktop;

namespace SIRASD.FinancialTests;

internal static class WpfTestHost
{
    private static App? _application;

    internal static void EnsureApplication()
    {
        if (Application.Current != null) return;
        _application = new App();
        _application.InitializeComponent();
    }
}
~~~

- [ ] **Step 2: Ejecutar y verificar fallo**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- patient UI
~~~

Expected: FAIL por columnas y diálogos ausentes.

- [ ] **Step 3: Añadir columnas sin volver editable toda la tabla**

MainWindow.xaml añade DataGridTemplateColumn. La casilla es clicable solo para pacientes activos; el botón Ajustar usa CanAdjustBalance:

~~~xml
<DataGridTextColumn Header="Saldo" Binding="{Binding BalanceDisplay}" Width="165"
                    ElementStyle="{StaticResource GridText}"/>
<DataGridTemplateColumn Header="Automático" Width="105">
  <DataGridTemplateColumn.CellTemplate>
    <DataTemplate>
      <CheckBox IsChecked="{Binding AutoDebtEnabled, Mode=OneWay}"
              IsEnabled="{Binding CanToggleAutomaticDebt}"
                Click="AutomaticDebt_Click"
                AutomationProperties.Name="Agregar deuda automáticamente"/>
    </DataTemplate>
  </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
<DataGridTemplateColumn Header="Ajustar" Width="100">
  <DataGridTemplateColumn.CellTemplate>
    <DataTemplate>
      <Button Content="Ajustar" Padding="10,5" MinHeight="30"
              IsEnabled="{Binding CanAdjustBalance}"
              Click="AdjustPatientBalance_Click"/>
    </DataTemplate>
  </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
~~~

- [ ] **Step 4: Implementar confirmación y recarga segura**

AutomaticDebt_Click debe recuperar el Patient desde DataContext, preguntar antes de guardar, llamar SetAutomaticDebt con DateTime.Now y siempre ejecutar RefreshAll en finally para que un clic cancelado o fallido no deje la casilla visualmente invertida.

- [ ] **Step 5: Implementar un único diálogo de ajuste**

BalanceAdjustmentDialog recibe Patient, DatabaseService y user. Muestra saldo actual, nuevo saldo, diferencia y motivo. Usa los estilos FieldLabel, Card y PrimaryButton. Save llama:

~~~csharp
_database.AdjustPatientBalance(
    new BalanceAdjustmentRequest(_patient.Id, newBalance, ReasonBox.Text.Trim()),
    DateTime.Now,
    _user);
~~~

El mismo diálogo se reutilizará desde pagos en Task 7.

- [ ] **Step 6: Mostrar finanzas en Editar paciente e historial**

Extender CenterForm con métodos concretos:

~~~csharp
public void AddInfo(string label, string value)
public Button AddAction(string title, Action action, bool primary = false)
~~~

MainWindow.Center.cs añade BalanceDisplay, AutoDebtDisplay, próxima/última cuota y botones de ajuste/historial. El ajuste se bloquea mediante patient.CanAdjustBalance.

- [ ] **Step 7: Ejecutar smoke test, build y prueba manual visual**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- patient UI
dotnet build .\SIRASD.Desktop.csproj -c Release
~~~

Manual: abrir Pacientes a 1000x680 y 1240x820, activar/desactivar una cuenta de prueba, comprobar que saldo y botones no se cortan.

- [ ] **Step 8: Commit**

~~~powershell
git add MainWindow.xaml MainWindow.xaml.cs MainWindow.Center.cs CenterForms.cs BalanceAdjustmentDialog.xaml BalanceAdjustmentDialog.xaml.cs BalanceHistoryDialog.xaml BalanceHistoryDialog.xaml.cs tests\SIRASD.FinancialTests\PatientUiSmokeTests.cs
git commit -m "feat: expose patient balances and automation"
~~~

### Task 7: Formulario de pagos ligado al saldo real

**Files:**
- Modify: PaymentsView.xaml
- Modify: PaymentsView.xaml.cs
- Create: ReasonDialog.xaml
- Create: ReasonDialog.xaml.cs
- Create: tests/SIRASD.FinancialTests/PaymentsUiSmokeTests.cs

**Interfaces:**
- Consumes: PaymentDraft.AppliesToBalance, SavePayment derivado, CancelPayment y BalanceAdjustmentDialog.
- Produces: resumen financiero del paciente, proyección protegida, cancelación auditada y actualización inmediata.

- [ ] **Step 1: Escribir smoke test fallido del formulario**

Verificar existencia y estado de:

- ApplyToBalanceCheck marcado inicialmente;
- PreviousBalanceBox y RemainingBalanceBox IsReadOnly=true;
- PatientBalanceSummary visible al seleccionar paciente;
- AdjustBalanceButton deshabilitado con AutoDebtEnabled=true;
- CancelPaymentButton deshabilitado sin selección.

- [ ] **Step 2: Ejecutar y verificar fallo**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- payments UI
~~~

Expected: FAIL por controles ausentes y cajas editables.

- [ ] **Step 3: Reemplazar captura manual por resumen calculado**

PaymentsView.xaml conserva las tarjetas y columnas actuales, añade una tarjeta Mint después de PatientBox y define:

~~~xml
<CheckBox x:Name="ApplyToBalanceCheck"
          Content="Aplicar este pago al saldo"
          IsChecked="True"
          Checked="BalanceInput_Changed"
          Unchecked="BalanceInput_Changed"/>
<TextBox x:Name="PreviousBalanceBox" IsReadOnly="True"/>
<TextBox x:Name="RemainingBalanceBox" IsReadOnly="True"/>
<Button x:Name="AdjustBalanceButton" Content="Ajustar saldo"
        Click="AdjustBalance_Click"/>
~~~

- [ ] **Step 4: Calcular solo una proyección en la interfaz**

PatientBox_SelectionChanged usa patient.BalanceCents / 100m como saldo anterior. UpdateRemainingBalance calcula:

~~~csharp
var remaining = ApplyToBalanceCheck.IsChecked == true ? previous - amount : previous;
~~~

SavePayment solo envía monto, concepto, periodo, medio, receptor, paciente y AppliesToBalance. Después recarga el paciente desde la base y muestra el saldo confirmado, no la proyección.

- [ ] **Step 5: Añadir cancelación con motivo**

ReasonDialog solicita un motivo de 3 a 500 caracteres. CancelPaymentButton requiere fila seleccionada y Status Vigente, confirma el efecto, llama CancelPayment y recarga historial/pacientes. Una fila cancelada conserva impresión y vista previa, pero el ticket se marca CANCELADO.

- [ ] **Step 6: Ejecutar pruebas y verificación visual**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- payments UI
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- payment.
dotnet build .\SIRASD.Desktop.csproj -c Release
~~~

Manual: comprobar pago menor, mayor, no aplicado y cancelado; revisar mensajes y bloqueo de ajuste.

- [ ] **Step 7: Commit**

~~~powershell
git add PaymentsView.xaml PaymentsView.xaml.cs ReasonDialog.xaml ReasonDialog.xaml.cs tests\SIRASD.FinancialTests\PaymentsUiSmokeTests.cs
git commit -m "feat: connect payment form to patient balance"
~~~

### Task 8: Consultas y cierre de caja

**Files:**
- Create: Services/DatabaseService.CashCuts.cs
- Modify: Models/CashCut.cs
- Create: tests/SIRASD.FinancialTests/CashCutTests.cs

**Interfaces:**
- Consumes: Payments, PaymentReversals y CashCutEligible.
- Produces: GetCashCutPreview(CashCutQuery), CloseCashCut(CashCutQuery, string notes, DateTime localNow, string user), GetCashCuts(), GetCashCut(string id), RecordCashCutPrint.

- [ ] **Step 1: Definir tipos públicos exactos**

CashCutQuery:

~~~csharp
public sealed record CashCutQuery(DateOnly From, DateOnly Through, bool PendingOnly = false)
{
    public DateTime FromInclusive => From.ToDateTime(TimeOnly.MinValue);
    public DateTime ToExclusive => Through.AddDays(1).ToDateTime(TimeOnly.MinValue);
}
~~~

CashCutLine incluye SourceType, SourceId, Folio, OccurredAt, PatientName, Concept, Period, PaymentMethod, AmountCents, Status y CashCutDisplayNumber. CashCutPreview expone Lines y totales por medio, ReversalCents, NetCents y EntryCount.

- [ ] **Step 2: Escribir pruebas fallidas de totalización y filtros**

Preparar pagos: 500 efectivo, 300 transferencia, 200 depósito, 100 tarjeta, 50 otro y cancelar el de 300. Verificar:

- total neto por cada medio, contando la reversión en el mismo medio original;
- ReversalCents=-30000;
- NetCents=85000;
- filtro inclusivo de fechas;
- AppliesToBalance no cambia totales;
- registros históricos CashCutEligible=0 aparecen en consulta general pero no en PendingOnly.

- [ ] **Step 3: Escribir pruebas fallidas de cierre y conflicto**

~~~csharp
yield return new("cut.close snapshots eligible entries once", () =>
{
    using var f = CashCutFixture.WithPayments();
    var query = new CashCutQuery(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), true);
    var cut = f.Database.CloseCashCut(query, "Corte semanal", new DateTime(2026, 9, 20, 18, 0, 0), "Dirección");
    TestAssert.True(cut.DisplayNumber.StartsWith("CORTE-20260920-"), "Unexpected cut number.");
    TestAssert.Throws<InvalidOperationException>(() =>
        f.Database.CloseCashCut(query, "Duplicado", new DateTime(2026, 9, 20, 18, 1, 0), "Dirección"));
});

yield return new("cut.cancel after close leaves original and enters next cut", () =>
{
    using var f = CashCutFixture.WithOneAppliedPayment();
    var first = f.CloseAll(new DateTime(2026, 9, 19, 12, 0, 0));
    var payment = f.Database.GetPayments().Single();
    f.Database.CancelPayment(payment.Id, "Corrección posterior", new DateTime(2026, 9, 20), "Dirección");
    var preserved = f.Database.GetCashCut(first.Id);
    var next = f.Database.GetCashCutPreview(new(new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20), true));
    TestAssert.Equal(1, preserved.Lines.Count);
    TestAssert.Equal(-FinancialRules.ToCents(payment.Amount, "monto", true), next.NetCents);
});
~~~

- [ ] **Step 4: Ejecutar y verificar fallo**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- cut.
~~~

Expected: compile FAIL por servicio ausente.

- [ ] **Step 5: Implementar una consulta UNION parametrizada**

La consulta une todos los pagos elegibles, incluidos los que después fueron cancelados, con sus reversiones y con límites >= FromInclusive y < ToExclusive. No filtra Payments por Status: el pago original es la entrada positiva y PaymentReversals es la salida negativa. Para PendingOnly excluye:

~~~sql
NOT EXISTS(
  SELECT 1 FROM CashCutLines l
  WHERE l.SourceType='Pago' AND l.SourceId=p.Id
)
~~~

y exige p.CashCutEligible=1. La rama de cancelación usa SourceType Cancelación y PaymentReversals.Id.

- [ ] **Step 6: Implementar CloseCashCut con revalidación dentro de transacción**

1. Validar From <= Through, usuario y notas <= 1000 caracteres.
2. Abrir transacción inmediata.
3. Volver a consultar movimientos pendientes.
4. Rechazar lista vacía.
5. Insertar CashCuts, obtener CutNumber y actualizar DisplayNumber CORTE-AAAAMMDD-NNN.
6. Insertar cada CashCutLine con instantánea.
7. Capturar violación UNIQUE como "Uno o más movimientos ya pertenecen a otro corte."
8. Auditar y confirmar.

- [ ] **Step 7: Probar cientos de registros y reloj**

Añadir 750 pagos en una base temporal y verificar que GetCashCutPreview devuelve los 750, sin el límite de GetPayments(500). Verificar que una fecha futura no entra en el corte del día actual.

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- cut.
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release
~~~

Expected: PASS.

- [ ] **Step 8: Commit**

~~~powershell
git add Models\CashCut.cs Services\DatabaseService.CashCuts.cs tests\SIRASD.FinancialTests\CashCutTests.cs
git commit -m "feat: add auditable cash cut service"
~~~

### Task 9: Pantalla de vista previa y cortes cerrados

**Files:**
- Create: CashCutsView.xaml
- Create: CashCutsView.xaml.cs
- Modify: PaymentsView.xaml
- Modify: PaymentsView.xaml.cs
- Create: tests/SIRASD.FinancialTests/CashCutsUiSmokeTests.cs

**Interfaces:**
- Consumes: DatabaseService cash-cut APIs.
- Produces: filtros Hoy/Semana/Mes/Personalizado, tarjetas por medio, tabla de folios, cierre, historial y selección de formato. Los botones de vista previa/impresión quedan visibles pero deshabilitados hasta que Task 11 conecte CashCutPrintService.

CashCutsView expone public void SetDatabase(DatabaseService database) y public void LoadData(string userName). PaymentsView.SetDatabase reenvía la base y PaymentsView.LoadData reenvía el usuario para que el cambio de cuenta siga aislando los datos.

- [ ] **Step 1: Escribir smoke test fallido de la vista**

Verificar que CashCutsView contiene:

- TodayFilterButton, WeekFilterButton, MonthFilterButton;
- FromDate y ThroughDate;
- PreviewGrid;
- CloseCutButton;
- ClosedCutsGrid;
- PreviewCutButton, PrintCutButton y ReprintCutButton;
- tarjetas CashTotalText, TransferTotalText, DepositTotalText, CardTotalText, OtherTotalText, ReversalTotalText y NetTotalText.

- [ ] **Step 2: Ejecutar y verificar fallo**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- cuts UI
~~~

Expected: FAIL por CashCutsView ausente.

- [ ] **Step 3: Construir la vista con recursos actuales**

CashCutsView.xaml reutiliza Card, Eyebrow, PrimaryButton, DataGrid, GridText, Green, Muted y Line. No define una paleta paralela. La tabla usa columnas Folio, Fecha y hora, Paciente, Concepto, Medio, Importe, Aplicado, Saldo y Estado.

- [ ] **Step 4: Integrarla dentro de Pagos y tickets**

PaymentsView raíz añade dos botones de subnavegación:

~~~xml
<Button x:Name="PaymentModeButton" Content="Registrar pagos y tickets"
        Style="{StaticResource PrimaryButton}" Click="ShowPayments_Click"/>
<Button x:Name="CashCutsModeButton" Content="Cortes de caja"
        Click="ShowCashCuts_Click"/>
~~~

La cuadrícula existente se nombra PaymentWorkspace. CashCutsView se incluye en el mismo UserControl y alterna Visibility; no se agrega un elemento nuevo a la barra lateral.

- [ ] **Step 5: Implementar filtros y confirmación del cierre**

Los atajos calculan:

- Hoy: From=Through=DateOnly.FromDateTime(DateTime.Today).
- Semana: lunes actual a domingo.
- Mes: día 1 a último día.
- Personalizado: fechas de DatePicker.

Antes de CloseCashCut, mostrar periodo, cantidad, primer/último folio, totales por medio, cancelaciones y neto. Tras cerrar, seleccionar el corte nuevo y conservar los tres botones de impresión deshabilitados; esta tarea entrega consulta/cierre completos y Task 11 los habilita cuando exista el renderizador verificado.

- [ ] **Step 6: Ejecutar smoke tests y revisión visual**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- cuts UI
dotnet build .\SIRASD.Desktop.csproj -c Release
~~~

Manual: revisar pantalla vacía, una semana, un mes y 750 registros; confirmar desplazamiento horizontal y vertical sin ocultar botones.

- [ ] **Step 7: Commit**

~~~powershell
git add CashCutsView.xaml CashCutsView.xaml.cs PaymentsView.xaml PaymentsView.xaml.cs tests\SIRASD.FinancialTests\CashCutsUiSmokeTests.cs
git commit -m "feat: add cash cut workspace"
~~~

### Task 10: Ticket individual con saldo, moneda y estado

**Files:**
- Modify: ReceiptPrintService.cs
- Modify: Models/Payment.cs
- Create: tests/SIRASD.FinancialTests/ReceiptDocumentTests.cs

**Interfaces:**
- Consumes: Payment con AppliesToBalance, Status, saldos y CashCutNumber.
- Produces: FixedDocument que comunica saldo/deuda/crédito, MXN, aplicación y cancelación sin alterar folio.

- [ ] **Step 1: Escribir pruebas fallidas que inspeccionan el árbol visual**

Crear helper que recorre FixedPage, Panel, Decorator y TextBlock y concatena Text/Inlines. Probar:

- COMPROBANTE DE PAGO aparece completo;
- TOTAL PAGADO y MXN aparecen;
- un saldo negativo se imprime como SALDO A FAVOR $200.00 MXN, no como -$200;
- pago no aplicado dice "Este pago no modificó el saldo";
- pago cancelado contiene CANCELADO y conserva PAG-000001;
- CashCutNumber aparece cuando existe;
- Thermal58 page width equivale a 48 mm y el contenido queda a 1 mm de ambos lados.

- [ ] **Step 2: Ejecutar y verificar fallo**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- receipt.
~~~

Expected: algunas aserciones nuevas FAIL.

- [ ] **Step 3: Actualizar el bloque financiero sin mostrar negativos ambiguos**

Reglas de texto:

~~~csharp
content.Children.Add(Line(
    payment.PreviousBalance < 0 ? "Saldo a favor anterior" : "Saldo anterior",
    BalanceText(payment.PreviousBalance),
    normal));
content.Children.Add(Block(
    payment.AppliesToBalance ? "PAGO APLICADO AL SALDO" : "ESTE PAGO NO MODIFICÓ EL SALDO",
    small, FontWeights.Bold, TextAlignment.Center));
content.Children.Add(Line(
    payment.RemainingBalance < 0 ? "Saldo a favor" : "Saldo restante",
    Math.Abs(payment.RemainingBalance).ToString("C2", Mexico) + " MXN",
    normal));
~~~

Añadir el helper concreto:

~~~csharp
private static string BalanceText(decimal value) =>
    Math.Abs(value).ToString("C2", Mexico) + " MXN";
~~~

Si Status es Cancelado, colocar CANCELADO antes del folio y una leyenda de que el comprobante no representa ingreso vigente. Si CashCutNumber no está vacío, imprimir "Corte: ...".

- [ ] **Step 4: Ejecutar tests y generar tres vistas previas verificables**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- receipt.
dotnet build .\SIRASD.Desktop.csproj -c Release
~~~

Expected: PASS. Abrir vista previa 58, 80 y Letter con nombre/concepto largos.

- [ ] **Step 5: Commit**

~~~powershell
git add ReceiptPrintService.cs Models\Payment.cs tests\SIRASD.FinancialTests\ReceiptDocumentTests.cs
git commit -m "feat: clarify balance on payment receipts"
~~~

### Task 11: Impresión térmica y PDF del corte completo

**Files:**
- Create: CashCutPrintService.cs
- Modify: CashCutsView.xaml
- Modify: CashCutsView.xaml.cs
- Create: tests/SIRASD.FinancialTests/CashCutPrintTests.cs

**Interfaces:**
- Consumes: CashCut instantáneo y ReceiptPaper.
- Produces: CashCutPrintService.CreateDocument(CashCut, ReceiptPaper), ShowPreview, Print y paginación determinista.

- [ ] **Step 1: Escribir pruebas fallidas de contenido y paginación**

Con un corte de 120 líneas, probar:

- todos los folios aparecen exactamente una vez;
- 58 mm crea más de una página/tramo y cada página contiene número de corte y Página X de N;
- 80 mm y Letter no recortan la última línea;
- TOTAL GENERAL MXN aparece una sola vez al final;
- Efectivo, Transferencia, Depósito, Tarjeta, Otros y Cancelaciones aparecen;
- nombres de 120 caracteres se envuelven;
- primera página contiene logo y CORTE DE CAJA;
- páginas 58 mm tienen ancho 48 mm y contenido 46 mm.

- [ ] **Step 2: Ejecutar y verificar fallo**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- cut print
~~~

Expected: compile FAIL porque CashCutPrintService no existe.

- [ ] **Step 3: Implementar composición por bloques medidos**

Firmas:

~~~csharp
internal static FixedDocument CreateDocument(CashCut cut, ReceiptPaper paper)
public static void ShowPreview(Window owner, CashCut cut, ReceiptPaper paper)
public static bool Print(Window owner, CashCut cut, ReceiptPaper paper, bool reprint)
~~~

Para 58 mm:

~~~csharp
var pageWidth = Mm(48);
var margin = Mm(1);
var contentWidth = Mm(46);
var maximumPageHeight = Mm(280);
~~~

Construir un bloque por línea de corte, medirlo con contentWidth y acumular hasta maximumPageHeight menos encabezado/pie. Crear las páginas después de conocer la cantidad total para imprimir Página X de N. Repetir encabezado corto en continuaciones. El logo completo solo aparece en la primera.

- [ ] **Step 4: Añadir resumen y firmas**

Al final de la última página añadir:

- totales por cada medio;
- cancelaciones;
- TOTAL GENERAL MXN en tamaño mayor;
- Entregó, Recibió y Firma;
- número de corte y leyenda SIRASD.

Para Letter usar 8.5 x 11 pulgadas y margen de 72 unidades WPF. Para 80 mm usar 80 mm con margen de 5 mm.

- [ ] **Step 5: Conectar vista previa, imprimir y reimprimir**

CashCutsView habilita PreviewCutButton, PrintCutButton y ReprintCutButton únicamente cuando existe un corte cerrado seleccionado, y llama CashCutPrintService con el formato elegido. Solo después de que PrintDialog acepte y PrintDocument sea invocado, llamar RecordCashCutPrint(cut.Id, user, paperName, reprint). Cancelar la ventana no escribe auditoría de impresión.

- [ ] **Step 6: Ejecutar pruebas de 120 y 750 folios**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- cut print
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release
dotnet build .\SIRASD.Desktop.csproj -c Release
~~~

Expected: PASS, todos los folios exactamente una vez, ninguna página supera ancho o alto definidos.

- [ ] **Step 7: Commit**

~~~powershell
git add CashCutPrintService.cs CashCutsView.xaml CashCutsView.xaml.cs tests\SIRASD.FinancialTests\CashCutPrintTests.cs
git commit -m "feat: print complete thermal cash cuts"
~~~

### Task 12: Ejecución automática, verificador, versión y aceptación

**Files:**
- Create: MainWindow.Finance.cs
- Modify: MainWindow.xaml.cs
- Modify: MainWindow.Accounts.cs
- Modify: App.xaml.cs
- Modify: SIRASD.Desktop.csproj
- Modify: Installer/SIRASD-Setup.iss
- Modify: ESTADO_DE_LA_VERSION.txt
- Modify: LEEME_PRIMERO.txt
- Create: tests/SIRASD.FinancialTests/EndToEndFinanceTests.cs

**Interfaces:**
- Consumes: todos los servicios, vistas e impresores anteriores.
- Produces: revisión al iniciar sesión/entrar en Pacientes o Pagos, revisión diaria en ejecución, verificador de instalación ampliado y versión 0.10.0.

- [ ] **Step 1: Escribir prueba integral fallida**

EndToEndFinanceTests debe:

1. Crear cuenta/base temporal.
2. Agregar paciente activo con cuota 500.
3. Activar automatización el sábado 19/09/2026.
4. Ejecutar revisión el lunes 05/10/2026 y comprobar tres cuotas.
5. Registrar pago 700 aplicado.
6. Registrar aportación 100 no aplicada.
7. Cerrar corte.
8. Cancelar el pago aplicado después del cierre.
9. Cerrar el segundo corte con la reversión.
10. Crear documentos 58 mm de pago y ambos cortes.
11. Reabrir la base y comprobar saldos, folios, cortes y ausencia de duplicados.

- [ ] **Step 2: Ejecutar y verificar el primer fallo de integración**

Run:

~~~powershell
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release -- end-to-end
~~~

Expected: FAIL hasta conectar la revisión automática y el verificador.

- [ ] **Step 3: Añadir monitor diario sin cobrar en sesión cerrada**

MainWindow.Finance.cs define DispatcherTimer y:

~~~csharp
private DateOnly? _lastFinanceCheck;

private void RunFinanceCheck()
{
    if (string.IsNullOrWhiteSpace(_currentUser)) return;
    var today = DateOnly.FromDateTime(DateTime.Now);
    if (_lastFinanceCheck == today) return;
    var result = App.Database.ApplyPendingWeeklyCharges(DateTime.Now, _currentUser);
    _lastFinanceCheck = today;
    if (result.ChargesCreated > 0) RefreshAll();
}
~~~

Llamar RunFinanceCheck después del login, antes de cargar Pacientes/Pagos y desde un DispatcherTimer cada 30 minutos. Al cerrar sesión, detener o ignorar mediante _currentUser vacío y reiniciar _lastFinanceCheck.

Para no impedir una segunda revisión necesaria tras cambios manuales el mismo día, SetAutomaticDebt y UpdatePatient llaman directamente al motor transaccional; el monitor diario es una defensa adicional.

- [ ] **Step 4: Ampliar --verify-installation**

App.xaml.cs debe verificar en base temporal:

- migración financiera;
- automatización y saldo;
- pago aplicado/no aplicado;
- cancelación;
- corte y segundo cierre;
- documentos de pago y corte para 58, 80 y Letter;
- creación de PaymentsView y CashCutsView;
- aislamiento de cuenta independiente.

El texto OK debe listar explícitamente "saldos, cargos semanales, cancelaciones y cortes".

- [ ] **Step 5: Actualizar versión y documentación operativa**

SIRASD.Desktop.csproj:

~~~xml
<Version>0.10.0</Version>
<AssemblyVersion>0.10.0.0</AssemblyVersion>
<FileVersion>0.10.0.0</FileVersion>
~~~

Actualizar Installer/SIRASD-Setup.iss a 0.10.0 y documentar:

- respaldo automático de migración;
- significado de modo automático/manual;
- corte vs reporte;
- formatos térmicos;
- necesidad de reiniciar una instancia antigua antes de probar.

- [ ] **Step 6: Ejecutar toda la verificación automatizada**

Run:

~~~powershell
dotnet clean .\SIRASD.Desktop.csproj -c Release
dotnet build .\SIRASD.Desktop.csproj -c Release
dotnet run --project .\tests\SIRASD.FinancialTests\SIRASD.FinancialTests.csproj -c Release
$verify = Join-Path $env:TEMP "sirasd-0100-verification.txt"
dotnet run --project .\SIRASD.Desktop.csproj -c Release -- --verify-installation $verify
Get-Content -LiteralPath $verify
dotnet publish .\SIRASD.Desktop.csproj -c Release
~~~

Expected: todos exit 0; verificador inicia con OK e incluye saldos/cargos/cortes.

- [ ] **Step 7: Inspección de base y sumas independientes**

En una base temporal de aceptación, comparar por consulta SQL:

~~~sql
SELECT SUM(AmountCents) FROM Payments WHERE CashCutEligible=1;
SELECT PaymentMethod,SUM(AmountCents) FROM Payments WHERE CashCutEligible=1 GROUP BY PaymentMethod;
SELECT SUM(AmountCents) FROM PaymentReversals;
SELECT SUM(NetCents) FROM CashCuts;
SELECT COUNT(*) FROM PatientBalanceMovements WHERE Type='Cuota semanal';
SELECT PatientId,WeekMonday,COUNT(*) FROM PatientBalanceMovements
WHERE Type='Cuota semanal' GROUP BY PatientId,WeekMonday HAVING COUNT(*)>1;
~~~

Expected: totales coinciden con pantalla/cortes y la última consulta devuelve cero filas.

- [ ] **Step 8: Verificación visual documentada**

Abrir la compilación nueva, no la instancia anterior, y revisar:

- Pacientes a 1000x680 y 1240x820.
- Pagos aplicados/no aplicados y cancelados.
- Cortes Hoy/Semana/Mes/Personalizado.
- Corte vacío, corto y de muchos folios.
- Vista previa 58 mm, 80 mm y Letter/PDF.
- Encabezados completos, logo centrado, MXN visible y márgenes simétricos.

Guardar capturas de las vistas previas en una carpeta de verificación fuera de Assets; no agregarlas al instalador.

- [ ] **Step 9: Prueba física separada**

Con la impresora térmica conectada:

1. Confirmar nombre de impresora y ancho configurado.
2. Reiniciar SIRASD 0.10.0.
3. Imprimir un comprobante de prueba de 58 mm.
4. Imprimir un corte con varios folios y al menos dos páginas/tramos.
5. Verificar logo, centrado, COMPROBANTE DE PAGO, CORTE DE CAJA, importes, MXN, alimentación y ausencia de truncamiento.
6. Registrar por separado si la impresión fue enviada, salió físicamente y fue revisada.

Si no está disponible la impresora, entregar build y vistas previas como verificados, dejando la prueba física expresamente pendiente.

- [ ] **Step 10: Commit final y revisión**

~~~powershell
git add MainWindow.Finance.cs MainWindow.xaml.cs MainWindow.Accounts.cs App.xaml.cs SIRASD.Desktop.csproj Installer ESTADO_DE_LA_VERSION.txt LEEME_PRIMERO.txt tests\SIRASD.FinancialTests
git commit -m "release: prepare SIRASD 0.10.0 financial controls"
git status --short
git log --oneline --decorate -12
~~~

Expected: árbol limpio y una secuencia de commits revisable por tarea.

## Orden de revisión durante la ejecución

Después de cada tarea:

1. Ejecutar la prueba específica.
2. Ejecutar el arnés completo.
3. Compilar Release.
4. Revisar únicamente el diff de esa tarea.
5. Corregir antes del commit si una prueba o revisión falla.
6. No avanzar con una base temporal dañada ni con el árbol sin entender.

## Resultado de entrega

La entrega final debe incluir:

- Código fuente 0.10.0.
- Base temporal de pruebas, nunca la base real del usuario.
- Resultados del arnés financiero.
- Resultado de --verify-installation.
- Ejecutable publicado e instalador si la herramienta de Inno Setup está disponible.
- Capturas o vistas previas verificadas para 58, 80 y Letter/PDF.
- Estado separado de la prueba física.
- Historial local de commits sin remoto.
