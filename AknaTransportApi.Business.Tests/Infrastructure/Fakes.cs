using System.Collections.Concurrent;

namespace AknaTransportApi.Business.Tests.Infrastructure;

/// <summary>Testlerde e-posta göndermek yerine kaydeder; isteğe bağlı olarak hata üretir.</summary>
public sealed class FakeEmailService : IEmailService
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();
    public bool Fail { get; set; }
    public int Attempts;

    public bool IsConfigured => true;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Attempts);
        if (Fail) throw new InvalidOperationException("SMTP sunucusuna bağlanılamadı (test).");
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

public enum FakeEArchiveMode
{
    Issue,
    Reject,
    Throw,
}

/// <summary>
/// Yalnızca testlerde kullanılan e-Arşiv adaptörü. Üretim kodunda kayıtlı değildir;
/// sağlayıcı yanıtlarına göre fatura akışının doğru davrandığını doğrulamak için kullanılır.
/// </summary>
public sealed class FakeEArchiveAdapter : IEArchiveProviderAdapter
{
    public const string ProviderName = "TestProvider";
    public static readonly byte[] PdfBytes = "%PDF-1.4\n% test e-arsiv\n"u8.ToArray();

    public FakeEArchiveMode Mode { get; set; } = FakeEArchiveMode.Issue;
    public int SendCount;

    public string Name => ProviderName;

    public Task<EArchiveSendResult> SendInvoiceAsync(EArchiveInvoiceRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref SendCount);
        return Mode switch
        {
            FakeEArchiveMode.Issue => Task.FromResult(new EArchiveSendResult(true, EArchiveDocumentStatus.Issued, request.InvoiceId.ToString(), "TST2026000000001", null, null)),
            FakeEArchiveMode.Reject => Task.FromResult(new EArchiveSendResult(false, EArchiveDocumentStatus.Rejected, null, null, null, "Alıcı VKN geçersiz.")),
            _ => throw new HttpRequestException("Sağlayıcıya ulaşılamadı."),
        };
    }

    public Task<EArchiveStatusResult> GetStatusAsync(string reference, CancellationToken cancellationToken) =>
        Task.FromResult(new EArchiveStatusResult(EArchiveDocumentStatus.Issued, null, null, null));

    public Task<byte[]> GetPdfAsync(string reference, CancellationToken cancellationToken) => Task.FromResult(PdfBytes);

    public Task<EArchiveCancelResult> CancelAsync(string reference, string reason, CancellationToken cancellationToken) =>
        Task.FromResult(new EArchiveCancelResult(true, null));
}
