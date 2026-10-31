using System.Globalization;

namespace AknaTransport.Infrastructure.Pdf;

internal static class PdfFormat
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>36.500,00 TL biçimi.</summary>
    public static string Money(decimal value, string currency = "TRY")
    {
        var suffix = currency == "TRY" ? "TL" : currency;
        return value.ToString("#,##0.00", Turkish) + " " + suffix;
    }

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", Turkish);

    public static string DateTime(DateTime value) => value.ToString("dd.MM.yyyy HH:mm", Turkish);

    public static string Period(DateOnly? start, DateOnly? end) => (start, end) switch
    {
        ({ } s, { } e) when s == e => Date(s),
        ({ } s, { } e) => $"{Date(s)} – {Date(e)}",
        ({ } s, null) => $"{Date(s)} ve sonrası",
        (null, { } e) => $"{Date(e)} ve öncesi",
        _ => "Tüm zamanlar",
    };
}
