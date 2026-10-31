using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Interfaces.Services;

public interface IReportService
{
    Task<JobReportDto> GetJobsAsync(JobReportPagedQuery query, CancellationToken ct);
    Task<JobReportSummaryDto> GetSummaryAsync(JobReportQuery query, CancellationToken ct);
    Task<FileDownload> GetJobsPdfAsync(JobReportQuery query, CancellationToken ct);
}
