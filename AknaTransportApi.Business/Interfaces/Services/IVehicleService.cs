namespace AknaTransportApi.Business.Interfaces.Services;

public interface IVehicleService
{
    Task<PagedResult<VehicleDto>> ListAsync(VehicleQuery query, CancellationToken ct);
    /// <summary>Oturumdaki kullanıcıya (şoför veya yönetici) aktif olarak atanmış araçlar.</summary>
    Task<IReadOnlyList<VehicleDto>> ListMineAsync(CancellationToken ct);
    Task<VehicleDto> GetAsync(Guid id, CancellationToken ct);
    Task<VehicleDto> CreateAsync(VehicleRequest request, CancellationToken ct);
    Task<VehicleDto> UpdateAsync(Guid id, VehicleRequest request, CancellationToken ct);
    Task<VehicleDto> AssignAsync(Guid id, AssignVehicleRequest request, CancellationToken ct);
    Task<VehicleDto> UnassignAsync(Guid id, CancellationToken ct);
}
