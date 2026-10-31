using System.Numerics;

namespace AknaTransportApi.Domain.Common;

/// <summary>IBAN biçimlendirme ve doğrulama (ISO 13616, mod-97).</summary>
public static class Iban
{
    /// <summary>Boşlukları kaldırır, büyük harfe çevirir. Boş girdi null döner.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c != '-')).ToUpperInvariant();
    }

    public static bool IsValid(string? value)
    {
        var iban = Normalize(value);
        if (iban is null || iban.Length is < 15 or > 34) return false;
        if (!iban.All(char.IsAsciiLetterOrDigit) || !char.IsAsciiLetter(iban[0]) || !char.IsAsciiLetter(iban[1])) return false;
        // Türkiye IBAN'ı 26 karakterdir.
        if (iban.StartsWith("TR", StringComparison.Ordinal) && iban.Length != 26) return false;

        var rearranged = iban[4..] + iban[..4];
        var digits = string.Concat(rearranged.Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString()));
        return BigInteger.Parse(digits) % 97 == 1;
    }

    /// <summary>Dörderli gruplar: "TR33 0006 1005 ...".</summary>
    public static string Format(string value)
    {
        var iban = Normalize(value) ?? string.Empty;
        return string.Join(" ", Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - i * 4))));
    }
}
