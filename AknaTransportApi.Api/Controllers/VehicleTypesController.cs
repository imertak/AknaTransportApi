using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransportApi.Api.Controllers;

/// <summary>Araç tipleri. Herkes listeleyebilir; ekleme ve düzenleme yöneticiye aittir. Tipler silinmez, pasife alınır.</summary>
[Route("api/vehicle-types")]
[Authorize]
public sealed class VehicleTypesController(IVehicleTypeService vehicleTypes) : ApiControllerBase
{
    /// <summary>Araç tipleri. includeInactive=true pasif tipleri de getirir (eski kayıtların adı için).</summary>
    [HttpGet]
    public async Task<ActionResult<BaseResponse<IReadOnlyList<VehicleTypeDto>>>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        Success(await vehicleTypes.ListAsync(includeInactive, ct));

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<VehicleTypeDto>>> Create(VehicleTypeRequest request, CancellationToken ct) =>
        Success(await vehicleTypes.CreateAsync(request, ct), "Araç tipi eklendi.");

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<VehicleTypeDto>>> Update(Guid id, VehicleTypeRequest request, CancellationToken ct) =>
        Success(await vehicleTypes.UpdateAsync(id, request, ct), "Araç tipi güncellendi.");
}
