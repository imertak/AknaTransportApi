namespace AknaTransportApi.Domain.Common;

/// <summary>Tutarı faturalardaki "Yalnız ..." satırı için Türkçe yazıya çevirir.</summary>
public static class AmountInWords
{
    private static readonly string[] Ones = ["", "BİR", "İKİ", "ÜÇ", "DÖRT", "BEŞ", "ALTI", "YEDİ", "SEKİZ", "DOKUZ"];
    private static readonly string[] Tens = ["", "ON", "YİRMİ", "OTUZ", "KIRK", "ELLİ", "ALTMIŞ", "YETMİŞ", "SEKSEN", "DOKSAN"];
    private static readonly string[] Scales = ["", "BİN", "MİLYON", "MİLYAR", "TRİLYON"];

    /// <summary>Ör. 9000.50 → "YALNIZ: DOKUZ BİN TÜRK LİRASI ELLİ KURUŞ".</summary>
    public static string Write(decimal amount, string currency = Money.DefaultCurrency)
    {
        var value = Money.Round(Math.Abs(amount));
        var whole = (long)decimal.Truncate(value);
        var cents = (int)((value - whole) * 100);
        var (main, sub) = CurrencyNames(currency);

        var text = $"YALNIZ: {Number(whole)} {main}";
        if (cents > 0) text += $" {Number(cents)} {sub}";
        return text;
    }

    public static string Number(long n)
    {
        if (n == 0) return "SIFIR";
        var parts = new List<string>();
        for (var scale = Scales.Length - 1; scale >= 0; scale--)
        {
            var divisor = (long)Math.Pow(1000, scale);
            var group = (int)(n / divisor % 1000);
            if (group == 0) continue;
            // "BİR BİN" denmez, yalnızca "BİN".
            if (scale == 1 && group == 1) parts.Add("BİN");
            else parts.Add((Hundreds(group) + " " + Scales[scale]).Trim());
        }
        return string.Join(" ", parts);
    }

    private static string Hundreds(int n)
    {
        var parts = new List<string>();
        var h = n / 100;
        var t = n % 100 / 10;
        var o = n % 10;
        if (h == 1) parts.Add("YÜZ");
        else if (h > 1) parts.Add($"{Ones[h]} YÜZ");
        if (t > 0) parts.Add(Tens[t]);
        if (o > 0) parts.Add(Ones[o]);
        return string.Join(" ", parts);
    }

    private static (string Main, string Sub) CurrencyNames(string currency) => currency.ToUpperInvariant() switch
    {
        "USD" => ("ABD DOLARI", "SENT"),
        "EUR" => ("AVRO", "SENT"),
        "GBP" => ("İNGİLİZ STERLİNİ", "PENİ"),
        _ => ("TÜRK LİRASI", "KURUŞ"),
    };
}
