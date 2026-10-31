namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>Nakliye işleri dökümü PDF üretimi (Infrastructure / QuestPDF).</summary>
public interface IJobReportPdfGenerator
{
    byte[] Generate(JobReportDocument document);
}
