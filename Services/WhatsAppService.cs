using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SIRASD.Desktop.Services;

public static class WhatsAppService
{
    public static Uri BuildChatUri(string phone, string message)
    {
        var digits = Regex.Replace(phone ?? "", "[^0-9]", "");
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.Length == 10) digits = "52" + digits;
        if (digits.Length is < 8 or > 15)
            throw new ArgumentException("El teléfono debe tener entre 8 y 15 dígitos. Para México puedes escribir los 10 dígitos habituales.");

        return new Uri($"https://wa.me/{digits}?text={Uri.EscapeDataString(message)}");
    }

    public static void OpenChat(string phone, string message)
    {
        var uri = BuildChatUri(phone, message);
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
