namespace AknaTransportApi.Business.Dtos.Integrations;

public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType = "application/pdf");

/// <param name="IdempotencyKey">Aynı e-postanın yeniden denemede iki kez gitmemesi için (destekleyen sağlayıcılarda, ör. Resend).</param>
public sealed record EmailMessage(string To, string? ToName, string Subject, string HtmlBody, string TextBody, IReadOnlyList<EmailAttachment> Attachments, string? IdempotencyKey = null)
{
    /// <summary>Bilgi (CC) alıcıları; müşterinin ek e-posta adresleri.</summary>
    public IReadOnlyList<string> Cc { get; init; } = [];
}
