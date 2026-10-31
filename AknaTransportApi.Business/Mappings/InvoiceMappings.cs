using FluentValidation;
using System.Linq.Expressions;

namespace AknaTransportApi.Business.Mappings;

public static class InvoiceMappings
{
    public static readonly Expression<Func<Invoice, InvoiceListItemDto>> ListProjection = i => new InvoiceListItemDto(
        i.Id, i.InvoiceNumber, i.CustomerId, i.Customer!.CompanyName, i.InvoiceDate, i.SubTotal, i.VatRate, i.VatAmount, i.GrandTotal,
        i.Currency, i.Status, i.Status == InvoiceStatus.Issued, i.EArchiveInvoiceNumber, i.Items.Count, i.IssuedAt, i.CreatedAt,
        i.Items.OrderBy(x => x.Id).Select(x => (Guid?)x.JobId).FirstOrDefault(),
        i.Items.OrderBy(x => x.Id).Select(x => x.Job!.JobNumber).FirstOrDefault(),
        i.Notes ?? i.Items.OrderBy(x => x.Id).Select(x => x.Description).FirstOrDefault(),
        i.Customer!.Email,
        i.WithholdingCode,
        i.WithholdingAmount,
        i.PayableAmount);
}
