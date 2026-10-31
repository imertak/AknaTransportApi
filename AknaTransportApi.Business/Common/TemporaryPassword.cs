namespace AknaTransportApi.Business.Common;

/// <summary>Kriptografik olarak güvenli, okunması kolay geçici şifre üretir.</summary>
public static class TemporaryPassword
{
    // Karışabilecek karakterler (0/O, 1/l/I) çıkarıldı.
    private const string Letters = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";

    public static string Generate(int length = 10)
    {
        if (length < 8) length = 8;
        var all = Letters + Digits;
        var chars = new char[length];
        chars[0] = Letters[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Letters.Length)];
        chars[1] = Digits[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Digits.Length)];
        for (var i = 2; i < length; i++) chars[i] = all[System.Security.Cryptography.RandomNumberGenerator.GetInt32(all.Length)];
        System.Security.Cryptography.RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
