using FluentValidation;
using System.Linq.Expressions;

namespace AknaTransportApi.Business.Mappings;

public static class InvoiceMappings
{
    public static readonly Expression<Func<Invoice, InvoiceListItemDto>> ListProjection = i => new InvoiceListItemDto(
        i.Id, i.InvoiceNumber, i.CustomerId, i.Customer!.CompanyName, i.InvoiceDate, i.SubTotal, i.VatRate, i.VatAmount, i.GrandTotal,
        i.Currency, i.Status, i.Status == InvoiceStatus.Issued, i.EArchiveInvoiceNumber, i.Items.Count(x => x.JobId != null), i.IssuedAt, i.CreatedAt,
        i.Items.Where(x => x.JobId != null).OrderBy(x => x.Id).Select(x => x.JobId).FirstOrDefault(),
        i.Items.Where(x => x.JobId != null).OrderBy(x => x.Id).Select(x => x.Job!.JobNumber).FirstOrDefault(),
        i.Notes ?? i.Items.OrderBy(x => x.Id).Select(x => x.Description).FirstOrDefault(),
        i.Customer!.Email,
        i.WithholdingCode,
        i.WithholdingAmount,
        i.PayableAmount,
        i.EArchiveProvider == Invoice.ManualProvider);
}
