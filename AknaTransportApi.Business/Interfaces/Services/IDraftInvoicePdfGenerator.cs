namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>Taslak fatura önizleme PDF'i. Üzerinde "resmi fatura değildir" ibaresi bulunur.</summary>
public interface IDraftInvoicePdfGenerator
{
    byte[] Generate(DraftInvoiceDocument document);
}
