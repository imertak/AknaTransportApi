using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Services.Customers;

/// <summary>
/// Cari hesap hesaplaması. Borç tarafı: kesilmiş (Issued) faturaların ödenecek tutarı (tevkifat düşülmüş) ile
/// faturasız borç kayıtları ve müşteriye yapılan iadeler. Alacak tarafı: tahsilatlar. Fatura kesilince bakiye
/// kendiliğinden artar, iptal edilince düşer; ayrıca kayıt tutulmaz.
/// </summary>
public sealed class CustomerAccountService(
    IRepository<Customer> customers,
    IRepository<Invoice> invoices,
    IRepository<CustomerTransaction> transactions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICustomerAccountService
{
    public async Task<CustomerBalanceOverviewDto> GetBalancesAsync(CustomerBalanceQuery query, CancellationToken ct)
    {
        var invoiceSums = await invoices.QueryAsNoTracking().Where(i => i.Status == InvoiceStatus.Issued)
            .GroupBy(i => i.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(i => i.PayableAmount), Count = g.Count(), Last = g.Max(i => i.InvoiceDate) })
            .ToDictionaryAsync(x => x.CustomerId, ct);
        var txSums = await transactions.QueryAsNoTracking()
            .GroupBy(t => new { t.CustomerId, t.Type })
            .Select(g => new { g.Key.CustomerId, g.Key.Type, Total = g.Sum(t => t.Amount), Last = g.Max(t => t.TransactionDate) })
            .ToListAsync(ct);
        var txByCustomer = txSums.ToLookup(x => x.CustomerId);

        var q = customers.QueryAsNoTracking();
        if (query.Search.TrimOrNull() is { } search)
        {
            var term = search.ToLower();
            q = q.Where(c => c.CompanyName.ToLower().Contains(term) || (c.TaxNumber != null && c.TaxNumber.Contains(term)));
        }
        var list = await q.Select(c => new { c.Id, c.CompanyName, c.IsActive }).ToListAsync(ct);

        var items = new List<CustomerBalanceDto>();
        foreach (var c in list)
        {
            invoiceSums.TryGetValue(c.Id, out var inv);
            var tx = txByCustomer[c.Id].ToList();
            // Pasif ve hiç hareketi olmayan müşteri listeyi kalabalıklaştırmasın.
            if (!c.IsActive && inv is null && tx.Count == 0) continue;
            decimal Sum(CustomerTransactionType type) => tx.Where(t => t.Type == type).Sum(t => t.Total);
            var dates = tx.Select(t => (DateOnly?)t.Last).Append(inv?.Last).Where(d => d is not null).ToList();
            items.Add(Build(c.Id, c.CompanyName, c.IsActive, inv?.Total ?? 0, inv?.Count ?? 0,
                Sum(CustomerTransactionType.Charge), Sum(CustomerTransactionType.Collection), Sum(CustomerTransactionType.Refund),
                dates.Count > 0 ? dates.Max() : null));
        }
        if (query.OpenOnly) items = items.Where(i => i.Balance != 0).ToList();

        // Borcu en yüksek müşteri üstte; bakiyesi olmayanlar ada göre.
        var ordered = items.OrderByDescending(i => i.Balance != 0).ThenByDescending(i => i.Balance).ThenBy(i => i.CompanyName).ToList();
        var totals = new CustomerBalanceTotalsDto(
            items.Sum(i => i.Invoiced), items.Sum(i => i.Charged), items.Sum(i => i.Collected), items.Sum(i => i.Refunded),
            items.Sum(i => i.Balance), items.Count(i => i.Balance != 0));
        return new CustomerBalanceOverviewDto(totals, ordered, await GetMonthsAsync(items.Select(i => i.CustomerId).ToHashSet(), ct));
    }

    /// <summary>Listede yer alan müşterilerin kesilen fatura, borç, tahsilat ve iade tutarları ay ay.</summary>
    private async Task<List<CustomerBalanceMonthDto>> GetMonthsAsync(HashSet<Guid> customerIds, CancellationToken ct)
    {
        var invoiceMonths = await invoices.QueryAsNoTracking().Where(i => i.Status == InvoiceStatus.Issued)
            .GroupBy(i => new { i.CustomerId, i.InvoiceDate.Year, i.InvoiceDate.Month })
            .Select(g => new { g.Key.CustomerId, g.Key.Year, g.Key.Month, Total = g.Sum(i => i.PayableAmount), Count = g.Count() })
            .ToListAsync(ct);
        var txMonths = await transactions.QueryAsNoTracking()
            .GroupBy(t => new { t.CustomerId, t.Type, t.TransactionDate.Year, t.TransactionDate.Month })
            .Select(g => new { g.Key.CustomerId, g.Key.Type, g.Key.Year, g.Key.Month, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct);

        var invoiceByMonth = invoiceMonths.Where(x => customerIds.Contains(x.CustomerId)).ToLookup(x => (x.Year, x.Month));
        var txByMonth = txMonths.Where(x => customerIds.Contains(x.CustomerId)).ToLookup(x => (x.Year, x.Month));
        return invoiceByMonth.Select(g => g.Key).Union(txByMonth.Select(g => g.Key))
            .OrderByDescending(k => k.Year).ThenByDescending(k => k.Month)
            .Select(k =>
            {
                var tx = txByMonth[k].ToList();
                decimal Sum(CustomerTransactionType type) => tx.Where(t => t.Type == type).Sum(t => t.Total);
                return new CustomerBalanceMonthDto(k.Year, k.Month, invoiceByMonth[k].Sum(i => i.Total), invoiceByMonth[k].Sum(i => i.Count),
                    Sum(CustomerTransactionType.Charge), Sum(CustomerTransactionType.Collection), Sum(CustomerTransactionType.Refund));
            })
            .ToList();
    }

    public async Task<CustomerLedgerDto> GetLedgerAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await customers.QueryAsNoTracking().Where(c => c.Id == customerId)
                           .Select(c => new { c.Id, c.CompanyName, c.IsActive }).FirstOrDefaultAsync(ct)
                       ?? throw NotFoundException.For("Müşteri");

        var invoiceRows = await invoices.QueryAsNoTracking().Where(i => i.CustomerId == customerId && i.Status == InvoiceStatus.Issued)
            .Select(i => new { i.Id, i.InvoiceDate, i.InvoiceNumber, i.EArchiveInvoiceNumber, i.PayableAmount, i.WithholdingAmount, CreatedAt = i.IssuedAt ?? i.CreatedAt })
            .ToListAsync(ct);
        var txRows = await transactions.QueryAsNoTracking().Where(t => t.CustomerId == customerId)
            .Select(t => new { t.Id, t.Type, t.Amount, t.TransactionDate, t.Method, t.Description, t.CreatedAt })
            .ToListAsync(ct);

        var raw = invoiceRows.Select(i => (Kind: LedgerEntryKind.Invoice, i.Id, Date: i.InvoiceDate,
                Title: $"Fatura {i.EArchiveInvoiceNumber ?? i.InvoiceNumber}",
                Description: i.WithholdingAmount > 0 ? "Tevkifat düşülmüş tutar" : null,
                Debit: i.PayableAmount, Credit: 0m, InvoiceId: (Guid?)i.Id, TransactionId: (Guid?)null, Method: (string?)null, i.CreatedAt))
            .Concat(txRows.Select(t => (Kind: KindOf(t.Type), t.Id, Date: t.TransactionDate,
                Title: TitleOf(t.Type), t.Description,
                Debit: t.Type == CustomerTransactionType.Collection ? 0m : t.Amount,
                Credit: t.Type == CustomerTransactionType.Collection ? t.Amount : 0m,
                InvoiceId: (Guid?)null, TransactionId: (Guid?)t.Id, t.Method, t.CreatedAt)))
            .OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).ToList();

        var running = 0m;
        var entries = new List<LedgerEntryDto>(raw.Count);
        foreach (var e in raw)
        {
            running += e.Debit - e.Credit;
            entries.Add(new LedgerEntryDto(e.Kind, e.Id, e.Date, e.Title, e.Description, e.Debit, e.Credit, running, e.InvoiceId, e.TransactionId, e.Method, e.CreatedAt));
        }
        entries.Reverse(); // en yeni hareket üstte

        decimal Sum(CustomerTransactionType type) => txRows.Where(t => t.Type == type).Sum(t => t.Amount);
        var last = raw.Count > 0 ? raw.Max(e => e.Date) : (DateOnly?)null;
        var balance = Build(customer.Id, customer.CompanyName, customer.IsActive, invoiceRows.Sum(i => i.PayableAmount), invoiceRows.Count,
            Sum(CustomerTransactionType.Charge), Sum(CustomerTransactionType.Collection), Sum(CustomerTransactionType.Refund), last);
        return new CustomerLedgerDto(balance, entries);
    }

    public async Task<CustomerTransactionDto> AddTransactionAsync(Guid customerId, CustomerTransactionRequest request, CancellationToken ct)
    {
        if (!await customers.QueryAsNoTracking().AnyAsync(c => c.Id == customerId, ct)) throw NotFoundException.For("Müşteri");
        var tx = new CustomerTransaction { CustomerId = customerId, CreatedByUserId = currentUser.UserId };
        Apply(tx, request);
        transactions.Add(tx);
        await unitOfWork.SaveChangesAsync(ct);
        return await GetTransactionAsync(tx.Id, ct);
    }

    public async Task<CustomerTransactionDto> UpdateTransactionAsync(Guid customerId, Guid transactionId, CustomerTransactionRequest request, CancellationToken ct)
    {
        var tx = await transactions.Query().FirstOrDefaultAsync(t => t.Id == transactionId && t.CustomerId == customerId, ct)
                 ?? throw NotFoundException.For("Hareket");
        Apply(tx, request);
        await unitOfWork.SaveChangesAsync(ct);
        return await GetTransactionAsync(tx.Id, ct);
    }

    public async Task DeleteTransactionAsync(Guid customerId, Guid transactionId, CancellationToken ct)
    {
        var tx = await transactions.Query().FirstOrDefaultAsync(t => t.Id == transactionId && t.CustomerId == customerId, ct)
                 ?? throw NotFoundException.For("Hareket");
        transactions.Remove(tx);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<CustomerTransactionDto> GetTransactionAsync(Guid id, CancellationToken ct) =>
        await transactions.QueryAsNoTracking().Where(t => t.Id == id)
            .Select(t => new CustomerTransactionDto(t.Id, t.CustomerId, t.Type, t.Amount, t.TransactionDate, t.Method, t.Description,
                t.CreatedByUser != null ? t.CreatedByUser.FullName : null, t.CreatedAt))
            .FirstAsync(ct);

    private static void Apply(CustomerTransaction tx, CustomerTransactionRequest request)
    {
        tx.Type = request.Type;
        tx.Amount = request.Amount;
        tx.TransactionDate = request.Date;
        tx.Method = request.Method.TrimOrNull();
        tx.Description = request.Description.TrimOrNull();
    }

    private static CustomerBalanceDto Build(Guid id, string name, bool active, decimal invoiced, int invoiceCount, decimal charged, decimal collected, decimal refunded, DateOnly? last) =>
        new(id, name, active, invoiced, invoiceCount, charged, collected, refunded, invoiced + charged + refunded - collected, last);

    private static LedgerEntryKind KindOf(CustomerTransactionType type) => type switch
    {
        CustomerTransactionType.Collection => LedgerEntryKind.Collection,
        CustomerTransactionType.Refund => LedgerEntryKind.Refund,
        _ => LedgerEntryKind.Charge,
    };

    private static string TitleOf(CustomerTransactionType type) => type switch
    {
        CustomerTransactionType.Collection => "Tahsilat",
        CustomerTransactionType.Refund => "Müşteriye ödeme / iade",
        _ => "Borç kaydı",
    };
}
