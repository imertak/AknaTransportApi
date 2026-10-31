using System.Net.Mail;

namespace AknaTransportApi.Business.Common;

public static class Text
{
    public static string? TrimOrNull(this string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return MailAddress.TryCreate(email.Trim(), out var address) && address.Address == email.Trim() && email.Contains('.', StringComparison.Ordinal);
    }
}
