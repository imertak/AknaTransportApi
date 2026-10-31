using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Infrastructure.Persistence;

/// <summary>
/// İş ve fatura numaralarını PostgreSQL sequence'leri ile üretir. Sequence işlemleri
/// transaction'dan bağımsız ve atomiktir; eşzamanlı isteklerde çakışma olmaz.
/// Biçim: AKN-2026-000001 (iş), AKF-2026-000001 (fatura). Benzersizlik ayrıca unique index ile korunur.
/// </summary>
public sealed class NumberGenerator(AppDbContext context) : INumberGenerator
{
    private static long _fallbackJob;
    private static long _fallbackInvoice;

    public async Task<string> NextJobNumberAsync(DateOnly jobDate, CancellationToken cancellationToken = default) =>
        $"AKN-{jobDate.Year}-{await NextAsync(AppDbContext.JobNumberSequence, cancellationToken):D6}";

    public async Task<string> NextInvoiceNumberAsync(DateOnly invoiceDate, CancellationToken cancellationToken = default) =>
        $"AKF-{invoiceDate.Year}-{await NextAsync(AppDbContext.InvoiceNumberSequence, cancellationToken):D6}";

    private async Task<long> NextAsync(string sequence, CancellationToken ct)
    {
        if (context.Database.IsNpgsql())
        {
            // Sequence adları sabittir; kullanıcı girdisi SQL'e eklenmez.
            var query = sequence == AppDbContext.JobNumberSequence
                ? context.Database.SqlQuery<long>($"SELECT nextval('job_number_seq') AS \"Value\"")
                : context.Database.SqlQuery<long>($"SELECT nextval('invoice_number_seq') AS \"Value\"");
            return await query.SingleAsync(ct);
        }

        // Sequence desteklemeyen sağlayıcılar (test ortamı) için süreç içi sayaç.
        return sequence == AppDbContext.JobNumberSequence
            ? Interlocked.Increment(ref _fallbackJob)
            : Interlocked.Increment(ref _fallbackInvoice);
    }
}
