namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>Müşteriye gönderilen kurumsal nakliye teklifi PDF'i.</summary>
public interface ITransportOfferPdfGenerator
{
    byte[] Generate(TransportOfferDocument document);
}
