using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransportApi.Api.Controllers;

/// <summary>
/// Araçlar ve şoför-araç atamaları. Yönetici tüm araçları yönetir; şoför yalnızca kendisine atanmış
/// araçları görür (<c>GET /api/vehicles/my</c>).
/// </summary>
[Route("api/vehicles")]
[Authorize]
public sealed class VehiclesController(IVehicleService vehicleService) : ApiControllerBase
{
    /// <summary>Araç listesi. Filtreler: driverId (o şoföre aktif atanmış araçlar), isActive, search.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<PagedResult<VehicleDto>>>> List([FromQuery] VehicleQuery query, CancellationToken ct) =>
        Success(await vehicleService.ListAsync(query, ct));

    /// <summary>Oturumdaki kullanıcının araçları (şoför ve yönetici).</summary>
    [HttpGet("my")]
    public async Task<ActionResult<BaseResponse<IReadOnlyList<VehicleDto>>>> Mine(CancellationToken ct) =>
        Success(await vehicleService.ListMineAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BaseResponse<VehicleDto>>> Get(Guid id, CancellationToken ct) =>
        Success(await vehicleService.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<VehicleDto>>> Create(VehicleRequest request, CancellationToken ct)
    {
        var vehicle = await vehicleService.CreateAsync(request, ct);
        return Created(nameof(Get), new { id = vehicle.Id }, vehicle, "Araç eklendi.");
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<VehicleDto>>> Update(Guid id, VehicleRequest request, CancellationToken ct) =>
        Success(await vehicleService.UpdateAsync(id, request, ct), "Araç güncellendi.");

    /// <summary>Aracı şoföre atar. Araç başka bir şoföre atanmışsa 409 döner.</summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<VehicleDto>>> Assign(Guid id, AssignVehicleRequest request, CancellationToken ct) =>
        Success(await vehicleService.AssignAsync(id, request, ct), "Araç şoföre atandı.");

    [HttpPost("{id:guid}/unassign")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<VehicleDto>>> Unassign(Guid id, CancellationToken ct) =>
        Success(await vehicleService.UnassignAsync(id, ct), "Araç ataması kaldırıldı.");
}
