using AknaTransportApi.Business.Dtos.Integrations;
using System.Globalization;
using System.Net;

namespace AknaTransportApi.Business.Services.Outbox;

/// <summary>Kurumsal Türkçe e-posta şablonları. Kullanıcı verisi HTML'e kodlanarak eklenir.</summary>
public static class EmailTemplates
{
    public const string InvoiceSubject = "AKNA Transport - Faturanız ve Nakliye İş Dökümünüz";
    public const string PasswordResetSubject = "AKNA Transport - Şifre Sıfırlama";
    private const string LogoContentId = "akna-logo";

    /// <summary>Şablon başlığındaki logo; her e-postaya satır içi görsel olarak eklenmelidir.</summary>
    public static EmailAttachment Logo { get; } = LoadLogo();

    private static EmailAttachment LoadLogo()
    {
        using var stream = typeof(EmailTemplates).Assembly.GetManifestResourceStream("AknaTransportApi.Business.akna-logo.png")
                           ?? throw new InvalidOperationException("AKNA logosu bulunamadı.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new EmailAttachment("akna-logo.png", buffer.ToArray(), "image/png") { ContentId = LogoContentId };
    }

    public static string DraftInvoiceSubject(string invoiceNumber) => $"AKNA Transport - Fatura Taslağı ve Nakliye İş Dökümü ({invoiceNumber})";

    public static (string Html, string Text) Invoice(InvoiceEmailModel m)
    {
        var amount = Money(m.Payable, m.Currency);
        var date = m.InvoiceDate.ToString("dd.MM.yyyy", Tr);
        var intro = m.Draft
            ? "AKNA Transport üzerinden gerçekleştirdiğiniz nakliye hizmetlerine ait fatura taslağınız hazırlanmıştır. Kontrolünüz için taslak fatura ve iş dökümü ektedir."
            : "AKNA Transport üzerinden gerçekleştirdiğiniz nakliye hizmetlerine ait faturanız kesilmiştir. Faturanızı ve ilgili nakliye işlerinin detaylı dökümünü ekte bulabilirsiniz.";
        const string draftNote = "Bu belge taslaktır ve resmi fatura yerine geçmez. Resmi e-Arşiv faturanız kesildiğinde ayrıca gönderilecektir.";
        var bank = m.Iban is null ? null : m.BankName is null ? m.Iban : $"{m.Iban} ({m.BankName})";

        var text = $"""
            Merhaba {m.CustomerName},

            {intro}
            {(m.Draft ? "\n" + draftNote + "\n" : "")}
            {(m.Draft ? "Taslak no" : "Fatura no")}: {m.InvoiceNumber}
            Tarih: {date}
            İş sayısı: {m.JobCount}
            Ara toplam: {Money(m.SubTotal, m.Currency)} + KDV
            KDV: {Money(m.VatAmount, m.Currency)}
            Ödenecek tutar: {amount}
            {(bank is null ? "" : $"IBAN: {bank}")}

            Ekler: {string.Join(", ", m.Attachments.Select(a => IsOriginal(a) ? $"{a} (GİB e-Arşiv portalındaki orijinal fatura)" : a))}

            Bizi tercih ettiğiniz için teşekkür ederiz.

            AKNA Transport
            """;

        var rows = new List<(string Label, string Value)>
        {
            (m.Draft ? "Taslak no" : "Fatura no", m.InvoiceNumber),
            ("Tarih", date),
            ("İş sayısı", $"{m.JobCount} adet"),
            ("Ara toplam", $"{Money(m.SubTotal, m.Currency)} + KDV"),
            ("KDV", Money(m.VatAmount, m.Currency)),
        };
        var rowsHtml = string.Concat(rows.Select(r => $"""
            <tr><td style="padding:8px 0;color:#6b6b70;font-size:13px">{Encode(r.Label)}</td><td align="right" style="padding:8px 0;font-size:13px;font-weight:600;color:#0b0b0c">{Encode(r.Value)}</td></tr>
            """));
        var badge = m.Draft
            ? """<span style="display:inline-block;padding:3px 10px;border-radius:999px;background:#fff4d6;color:#7a5600;font-size:11px;font-weight:700;letter-spacing:1px">TASLAK</span>"""
            : """<span style="display:inline-block;padding:3px 10px;border-radius:999px;background:#e3f5e9;color:#16623a;font-size:11px;font-weight:700;letter-spacing:1px">E-ARŞİV FATURA</span>""";
        var attachments = string.Concat(m.Attachments.Select(a => $"""
            <tr><td style="padding:6px 0;font-size:13px;color:#0b0b0c">&#128206;&nbsp; {Encode(a)}{(IsOriginal(a) ? "<br><span style=\"color:#6b6b70;font-size:12px;padding-left:22px\">GİB e-Arşiv portalındaki orijinal fatura; tarayıcıda açılır.</span>" : "")}</td></tr>
            """));

        var html = Wrap($"""
            <p style="margin:0 0 16px">Merhaba <strong>{Encode(m.CustomerName)}</strong>,</p>
            <p style="margin:0 0 20px;color:#333">{Encode(intro)}</p>
            {(m.Draft ? $"""<div style="margin:0 0 20px;padding:12px 14px;border-left:3px solid #e0a800;background:#fffaf0;color:#5c4400;font-size:13px">{Encode(draftNote)}</div>""" : "")}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #e6e6e8;border-radius:10px;border-collapse:separate">
              <tr><td style="padding:16px 18px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                  <tr><td style="padding-bottom:10px;font-size:12px;color:#6b6b70;letter-spacing:1px">FATURA ÖZETİ</td><td align="right" style="padding-bottom:10px">{badge}</td></tr>
                  {rowsHtml}
                </table>
              </td></tr>
              <tr><td style="padding:14px 18px;background:#0b0b0c;border-radius:0 0 9px 9px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
                  <td style="color:#fff;font-size:14px">Ödenecek tutar</td>
                  <td align="right" style="color:#fff;font-size:18px;font-weight:700">{Encode(amount)}</td>
                </tr></table>
              </td></tr>
            </table>
            {(bank is null ? "" : $"""<p style="margin:16px 0 0;font-size:13px;color:#333">Ödeme için IBAN: <strong style="font-family:monospace">{Encode(bank)}</strong></p>""")}
            <p style="margin:24px 0 6px;font-size:12px;color:#6b6b70;letter-spacing:1px">EKLER</p>
            <table role="presentation" cellpadding="0" cellspacing="0">{attachments}</table>
            <p style="margin:24px 0 0;color:#333">Bizi tercih ettiğiniz için teşekkür ederiz.</p>
            <p style="margin:4px 0 0"><strong>AKNA Transport</strong></p>
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

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static string Money(decimal value, string currency) =>
        currency is "TRY" or "TL" ? $"{value.ToString("N2", Tr)} TL" : $"{value.ToString("N2", Tr)} {currency}";

    private static bool IsOriginal(string fileName) => fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Wrap(string body) => $"""
        <!DOCTYPE html>
        <html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;padding:0;background:#f3f3f5;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:14px;line-height:1.55;color:#0b0b0c">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f3f3f5">
          <tr><td align="center" style="padding:28px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e6e6e8">
              <tr><td align="center" style="background:#000000;padding:28px 28px 24px">
                <img src="cid:{LogoContentId}" width="160" height="38" alt="AKNA" style="display:block;border:0;width:160px;height:38px;color:#ffffff;font-size:28px;font-weight:800;letter-spacing:8px">
                <div style="color:#8a8a90;font-size:10px;font-weight:600;letter-spacing:5px;margin-top:10px">TRANSPORT</div>
              </td></tr>
              <tr><td style="padding:28px">
              {body}
              </td></tr>
              <tr><td style="padding:16px 28px;background:#fafafb;border-top:1px solid #eeeef0;color:#8a8a90;font-size:11px">
                Bu e-posta AKNA Transport sistemi tarafından otomatik olarak gönderilmiştir.
              </td></tr>
            </table>
          </td></tr>
        </table>
        </body></html>
        """;
}

/// <summary>Fatura e-postası şablonunun verisi. Payable: ödenecek tutar (KDV dahil, tevkifat düşülmüş).</summary>
public sealed record InvoiceEmailModel(
    string CustomerName,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    int JobCount,
    decimal SubTotal,
    decimal VatAmount,
    decimal Payable,
    string Currency,
    string? Iban,
    string? BankName,
    bool Draft,
    IReadOnlyList<string> Attachments);
