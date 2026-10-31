using System.Net;

namespace AknaTransport.Business.Features.Outbox;

/// <summary>Kurumsal Türkçe e-posta şablonları. Kullanıcı verisi HTML'e kodlanarak eklenir.</summary>
public static class EmailTemplates
{
    public const string InvoiceSubject = "AKNA Transport - Faturanız ve Nakliye İş Dökümünüz";
    public const string PasswordResetSubject = "AKNA Transport - Şifre Sıfırlama";

    public static (string Html, string Text) Invoice(string customerName)
    {
        var text = $"""
            Merhaba {customerName},

            AKNA Transport üzerinden gerçekleştirdiğiniz nakliye hizmetlerine ait faturanız oluşturulmuştur.

            Faturanızı ve ilgili nakliye işlerinin detaylı dökümünü ekte bulabilirsiniz.

            Bizi tercih ettiğiniz için teşekkür ederiz.

            AKNA Transport
            """;

        var html = Wrap($"""
            <p>Merhaba {Encode(customerName)},</p>
            <p>AKNA Transport üzerinden gerçekleştirdiğiniz nakliye hizmetlerine ait faturanız oluşturulmuştur.</p>
            <p>Faturanızı ve ilgili nakliye işlerinin detaylı dökümünü ekte bulabilirsiniz.</p>
            <p>Bizi tercih ettiğiniz için teşekkür ederiz.</p>
            <p style="margin-top:24px"><strong>AKNA Transport</strong></p>
            """);
        return (html, text);
    }

    public static (string Html, string Text) PasswordReset(string fullName, string token, string? link, DateTime expiresAtLocal)
    {
        var expires = expiresAtLocal.ToString("dd.MM.yyyy HH:mm");
        var action = link is null
            ? $"Şifre sıfırlama kodunuz: {token}"
            : $"Şifrenizi sıfırlamak için bağlantıyı kullanın: {link}";
        var text = $"""
            Merhaba {fullName},

            Hesabınız için şifre sıfırlama talebi aldık.

            {action}

            Bu bağlantı / kod {expires} tarihine kadar geçerlidir. Talebi siz yapmadıysanız bu e-postayı dikkate almayın.

            AKNA Transport
            """;

        var htmlAction = link is null
            ? $"<p>Şifre sıfırlama kodunuz:</p><p style=\"font-family:monospace;font-size:16px\">{Encode(token)}</p>"
            : $"<p><a href=\"{Encode(link)}\">Şifremi sıfırla</a></p>";
        var html = Wrap($"""
            <p>Merhaba {Encode(fullName)},</p>
            <p>Hesabınız için şifre sıfırlama talebi aldık.</p>
            {htmlAction}
            <p>Bu bağlantı / kod {expires} tarihine kadar geçerlidir. Talebi siz yapmadıysanız bu e-postayı dikkate almayın.</p>
            <p style="margin-top:24px"><strong>AKNA Transport</strong></p>
            """);
        return (html, text);
    }

    public static string CredentialsSms(string fullName, string email, string password, bool isReset) => isReset
        ? $"AKNA Transport: Sayın {fullName}, şifreniz sıfırlandı. Geçici şifreniz: {password} . İlk girişte yeni şifre belirlemeniz istenecektir."
        : $"AKNA Transport: Sayın {fullName}, hesabınız oluşturuldu. Kullanıcı adı: {email} Geçici şifre: {password} . İlk girişte yeni şifre belirlemeniz istenecektir.";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Wrap(string body) => $"""
        <!DOCTYPE html>
        <html lang="tr"><head><meta charset="utf-8"></head>
        <body style="font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111;line-height:1.5">
        <div style="max-width:600px;margin:0 auto;padding:24px;border:1px solid #ddd">
        <div style="font-size:18px;font-weight:bold;letter-spacing:1px;border-bottom:2px solid #111;padding-bottom:8px;margin-bottom:16px">AKNA TRANSPORT</div>
        {body}
        </div>
        </body></html>
        """;
}
