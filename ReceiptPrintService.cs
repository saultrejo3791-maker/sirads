using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SIRASD.Desktop.Models;

namespace SIRASD.Desktop;

internal enum ReceiptPaper
{
    Thermal58,
    Thermal80,
    Letter
}

internal static class ReceiptPrintService
{
    private static readonly CultureInfo Mexico = new("es-MX");

    public static string PaperName(ReceiptPaper paper) => paper switch
    {
        ReceiptPaper.Thermal58 => "Ticket térmico de 58 mm",
        ReceiptPaper.Thermal80 => "Ticket térmico de 80 mm",
        _ => "Hoja normal / PDF"
    };

    public static bool Print(Window owner, Payment payment, ReceiptPaper paper, bool reprint)
    {
        var dialog = new PrintDialog();
        if (paper == ReceiptPaper.Letter)
            dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.NorthAmericaLetter);
        if (dialog.ShowDialog() != true) return false;
        var document = CreateDocument(payment, paper, reprint);
        dialog.PrintDocument(document.DocumentPaginator, $"SIRASD · {payment.Folio}");
        return true;
    }

    public static void ShowPreview(Window owner, Payment payment, ReceiptPaper paper, bool reprint = false)
    {
        var viewer = new DocumentViewer { Document = CreateDocument(payment, paper, reprint), Margin = new Thickness(12) };
        var window = new Window
        {
            Owner = owner, Title = $"Vista previa · {payment.Folio}", Width = paper == ReceiptPaper.Letter ? 900 : 620,
            Height = 780, MinWidth = 500, MinHeight = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White, Content = viewer
        };
        window.Loaded += (_, _) => viewer.FitToWidth();
        window.ShowDialog();
    }

    internal static FixedDocument CreateDocument(Payment payment, ReceiptPaper paper, bool reprint)
    {
        // El rollo mide 58 mm, pero el cabezal POS-58 solo expone 48 mm imprimibles (384 puntos a 203 dpi).
        var pageWidth = paper switch { ReceiptPaper.Thermal58 => Mm(48), ReceiptPaper.Thermal80 => Mm(80), _ => 8.5 * 96 };
        var minimumHeight = paper == ReceiptPaper.Letter ? 11 * 96 : Mm(115);
        var margin = paper switch { ReceiptPaper.Thermal58 => Mm(1), ReceiptPaper.Thermal80 => Mm(5), _ => 72 };
        var contentWidth = pageWidth - margin * 2;
        var normal = paper switch { ReceiptPaper.Thermal58 => 9.2, ReceiptPaper.Thermal80 => 10.2, _ => 12.0 };
        var small = normal - 1.2;
        var content = new StackPanel { Width = contentWidth, Background = Brushes.White };

        content.Children.Add(Logo(paper));
        content.Children.Add(Block(payment.CenterName, normal + 1.2, FontWeights.Bold, TextAlignment.Center));
        if (!string.IsNullOrWhiteSpace(payment.CenterAddress)) content.Children.Add(Block(payment.CenterAddress, small, FontWeights.Normal, TextAlignment.Center));
        if (!string.IsNullOrWhiteSpace(payment.CenterPhone)) content.Children.Add(Block($"Tel. {payment.CenterPhone}", small, FontWeights.Normal, TextAlignment.Center));
        if (!string.IsNullOrWhiteSpace(payment.CenterRfc)) content.Children.Add(Block($"RFC: {payment.CenterRfc}", small, FontWeights.Normal, TextAlignment.Center));
        content.Children.Add(Separator());
        content.Children.Add(Block("COMPROBANTE DE PAGO", normal + (paper == ReceiptPaper.Letter ? 5 : 4.5), FontWeights.Bold, TextAlignment.Center));
        if (reprint) content.Children.Add(Block("REIMPRESIÓN", normal, FontWeights.Bold, TextAlignment.Center));
        var identification = Block($"Folio: {payment.Folio}\nFecha y hora: {payment.PaidAt:dd/MM/yyyy HH:mm}", normal, FontWeights.SemiBold, TextAlignment.Center);
        identification.Margin = new Thickness(0, 5, 0, 3); content.Children.Add(identification);
        content.Children.Add(Separator());
        content.Children.Add(Line("Nombre del usuario", payment.PatientName, normal));
        content.Children.Add(Line("Folio del usuario", payment.PatientFolio, normal));
        content.Children.Add(Line("Concepto", payment.Concept, normal));
        content.Children.Add(Line("Periodo / semana", payment.Period, normal));
        content.Children.Add(Line("Forma de pago", payment.PaymentMethod, normal));
        content.Children.Add(Separator());
        var totalLabel = Block("TOTAL PAGADO", normal, FontWeights.Bold, TextAlignment.Center);
        totalLabel.Margin = new Thickness(0, 4, 0, 0); content.Children.Add(totalLabel);
        var amount = Block($"{payment.Amount.ToString("C2", Mexico)} MXN", normal + (paper == ReceiptPaper.Letter ? 9 : 5.5), FontWeights.Bold, TextAlignment.Center);
        amount.Margin = new Thickness(0, 1, 0, 6); content.Children.Add(amount);
        content.Children.Add(Line("Saldo anterior", payment.PreviousBalance.ToString("C2", Mexico), normal));
        content.Children.Add(Line("Saldo restante", payment.RemainingBalance.ToString("C2", Mexico), normal));
        content.Children.Add(Separator());
        content.Children.Add(Line("Recibió", payment.ReceivedBy, normal));
        content.Children.Add(Line("Registró", payment.RegisteredBy, normal));
        var signature = Block("\n____________________________\nFirma de quien recibe", small, FontWeights.Normal, TextAlignment.Center);
        signature.Margin = new Thickness(0, paper == ReceiptPaper.Letter ? 34 : 17, 0, 8); content.Children.Add(signature);
        if (!string.IsNullOrWhiteSpace(payment.ReceiptFooter)) content.Children.Add(Block(payment.ReceiptFooter, small, FontWeights.Normal, TextAlignment.Center));
        content.Children.Add(Block("Conserve este comprobante.", small, FontWeights.Normal, TextAlignment.Center));

        content.Measure(new Size(contentWidth, double.PositiveInfinity));
        var pageHeight = Math.Max(minimumHeight, content.DesiredSize.Height + margin * 2);
        content.Arrange(new Rect(0, 0, contentWidth, content.DesiredSize.Height));
        var page = new FixedPage { Width = pageWidth, Height = pageHeight, Background = Brushes.White };
        FixedPage.SetLeft(content, margin); FixedPage.SetTop(content, margin); page.Children.Add(content);
        var pageContent = new PageContent(); ((IAddChild)pageContent).AddChild(page);
        var document = new FixedDocument(); document.Pages.Add(pageContent);
        return document;
    }

    private static Image Logo(ReceiptPaper paper)
    {
        var size = paper switch { ReceiptPaper.Thermal58 => Mm(40), ReceiptPaper.Thermal80 => Mm(44), _ => Mm(48) };
        var image = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/SIRASD;component/Assets/CentroLogoTicket.png", UriKind.Absolute)),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, paper == ReceiptPaper.Letter ? 10 : 5)
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static TextBlock Block(string text, double size, FontWeight weight, TextAlignment alignment) => new()
    {
        Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = size, FontWeight = weight,
        TextAlignment = alignment, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black, Margin = new Thickness(0, 1.5, 0, 1.5)
    };

    private static TextBlock Line(string label, string value, double size)
    {
        var block = Block("", size, FontWeights.Normal, TextAlignment.Left);
        block.Inlines.Add(new Run(label + ": ") { FontWeight = FontWeights.SemiBold });
        block.Inlines.Add(new Run(string.IsNullOrWhiteSpace(value) ? "—" : value));
        return block;
    }

    private static Border Separator() => new() { Height = 1, Background = Brushes.Black, Margin = new Thickness(0, 7, 0, 7) };
    private static double Mm(double millimeters) => millimeters / 25.4 * 96;
}
