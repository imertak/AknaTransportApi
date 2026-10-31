namespace AknaTransportApi.Business.Dtos.Integrations;

public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType = "application/pdf");

public sealed record EmailMessage(string To, string? ToName, string Subject, string HtmlBody, string TextBody, IReadOnlyList<EmailAttachment> Attachments);
