using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AknaTransportApi.Business.Interfaces.Services;

public interface IJobService
{
    Task<PagedResult<JobDto>> ListAsync(JobQuery query, CancellationToken ct);
    Task<PagedResult<JobDto>> ListMyJobsAsync(JobQuery query, CancellationToken ct);
    Task<JobDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<JobStatusHistoryDto>> GetHistoryAsync(Guid id, CancellationToken ct);
    Task<JobDetailDto> CreateAsync(CreateJobRequest request, CancellationToken ct);
    Task<JobDetailDto> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<JobDetailDto> AssignAsync(Guid id, AssignJobRequest request, CancellationToken ct);
    Task<JobDetailDto> ClaimAsync(Guid id, ClaimJobRequest request, CancellationToken ct);
    Task<JobDetailDto> AcceptAsync(Guid id, JobActionRequest request, CancellationToken ct);
    Task<JobDetailDto> StartAsync(Guid id, JobActionRequest request, CancellationToken ct);
    Task<JobDetailDto> StartLoadingAsync(Guid id, JobActionRequest request, CancellationToken ct);
    Task<JobDetailDto> StartDeliveryAsync(Guid id, JobActionRequest request, CancellationToken ct);
    Task<JobDetailDto> CompleteAsync(Guid id, JobActionRequest request, CancellationToken ct);
    Task<JobDetailDto> CancelAsync(Guid id, CancelJobRequest request, CancellationToken ct);
    IQueryable<TransportJob> ApplyFilters(IQueryable<TransportJob> q, JobQuery query);
}
