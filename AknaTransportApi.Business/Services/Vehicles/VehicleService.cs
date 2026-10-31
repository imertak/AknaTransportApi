using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AknaTransportApi.Business.Services.Vehicles;

/// <summary>
/// Araç ve şoför-araç atamaları. Bir şoförün birden fazla aracı olabilir; bir araç aynı anda yalnızca
/// bir şoföre atanabilir. Bu kural servis katmanında kontrol edilir ve veritabanında filtreli benzersiz
/// indeksle (driver_vehicles.VehicleId WHERE IsActive) garanti edilir.
/// </summary>
public sealed class VehicleService(
    IRepository<Vehicle> vehicles,
    IRepository<DriverVehicle> assignments,
    IRepository<User> users,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<VehicleService> logger) : IVehicleService
{
    public async Task<PagedResult<VehicleDto>> ListAsync(VehicleQuery query, CancellationToken ct)
    {
        EnsureAdmin();
        var q = vehicles.QueryAsNoTracking();
        if (query.IsActive is { } active) q = q.Where(v => v.IsActive == active);
        if (query.DriverId is { } driverId) q = q.Where(v => v.DriverAssignments.Any(a => a.IsActive && a.DriverId == driverId));
        if (query.Search.TrimOrNull() is { } search)
        {
            var s = search.ToLower();
            q = q.Where(v => v.PlateNumber.ToLower().Contains(s) || (v.Brand != null && v.Brand.ToLower().Contains(s)) || (v.Model != null && v.Model.ToLower().Contains(s)));
        }
        return await q.OrderBy(v => v.PlateNumber).Select(VehicleMappings.Projection).ToPagedResultAsync(query, ct);
    }

    public async Task<IReadOnlyList<VehicleDto>> ListMineAsync(CancellationToken ct) =>
        await vehicles.QueryAsNoTracking()
            .Where(v => v.DriverAssignments.Any(a => a.IsActive && a.DriverId == currentUser.UserId))
            .OrderByDescending(v => v.IsActive).ThenBy(v => v.PlateNumber)
            .Select(VehicleMappings.Projection).ToListAsync(ct);

    public async Task<VehicleDto> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await vehicles.QueryAsNoTracking().Where(v => v.Id == id).Select(VehicleMappings.Projection).FirstOrDefaultAsync(ct)
                  ?? throw NotFoundException.For("Araç");
        // Şoför yalnızca kendisine atanmış aracı görebilir.
        if (!currentUser.IsAdmin && dto.DriverId != currentUser.UserId) throw new ForbiddenException("Bu aracı görüntüleme yetkiniz yok.");
        return dto;
    }

    public async Task<VehicleDto> CreateAsync(VehicleRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var vehicle = new Vehicle { IsActive = true };
        vehicle.Apply(request);
        await EnsurePlateAvailableAsync(vehicle.PlateNumber, null, ct);
        vehicles.Add(vehicle);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Araç oluşturuldu. VehicleId: {VehicleId}", vehicle.Id);
        return await GetAsync(vehicle.Id, ct);
    }

    public async Task<VehicleDto> UpdateAsync(Guid id, VehicleRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var vehicle = await vehicles.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Araç");
        vehicle.Apply(request);
        await EnsurePlateAvailableAsync(vehicle.PlateNumber, id, ct);
        // Pasife alınan araç mevcut işlerde kalır ama yeni işlere atanamaz.
        vehicle.IsActive = request.IsActive;
        await unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<VehicleDto> AssignAsync(Guid id, AssignVehicleRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var vehicle = await vehicles.QueryAsNoTracking().Where(v => v.Id == id).Select(v => new { v.IsActive }).FirstOrDefaultAsync(ct)
                      ?? throw NotFoundException.For("Araç");
        if (!vehicle.IsActive) throw new ConflictException("Pasif araç şoföre atanamaz.");
        if (!await users.QueryAsNoTracking().AnyAsync(u => u.Id == request.DriverId && u.IsActive, ct))
            throw new BusinessValidationException("Seçilen şoför bulunamadı veya pasif durumda.", [new ErrorDetail("driverId", "Geçerli bir şoför seçin.")]);

        var current = await assignments.Query().FirstOrDefaultAsync(a => a.VehicleId == id && a.IsActive, ct);
        if (current is not null)
        {
            if (current.DriverId == request.DriverId) return await GetAsync(id, ct);
            throw new ConflictException("Araç başka bir şoföre atanmış. Önce mevcut atamayı kaldırın.");
        }

        assignments.Add(new DriverVehicle
        {
            DriverId = request.DriverId,
            VehicleId = id,
            IsActive = true,
            AssignedAt = Now,
            AssignedByUserId = currentUser.UserId,
        });
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı iki atama: filtreli benzersiz indeks ikincisini reddeder.
            throw new ConflictException("Araç aynı anda başka bir şoföre atandı. Listeyi yenileyip tekrar deneyin.");
        }
        logger.LogInformation("Araç şoföre atandı. VehicleId: {VehicleId}, DriverId: {DriverId}", id, request.DriverId);
        return await GetAsync(id, ct);
    }

    public async Task<VehicleDto> UnassignAsync(Guid id, CancellationToken ct)
    {
        EnsureAdmin();
        if (!await vehicles.QueryAsNoTracking().AnyAsync(v => v.Id == id, ct)) throw NotFoundException.For("Araç");
        var current = await assignments.Query().FirstOrDefaultAsync(a => a.VehicleId == id && a.IsActive, ct);
        if (current is not null)
        {
            current.IsActive = false;
            current.UnassignedAt = Now;
            await unitOfWork.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    private async Task EnsurePlateAvailableAsync(string plate, Guid? exceptId, CancellationToken ct)
    {
        if (await vehicles.QueryAsNoTracking().AnyAsync(v => v.PlateNumber == plate && v.Id != exceptId, ct))
            throw new ConflictException($"{plate} plakalı araç zaten kayıtlı.");
    }

    private void EnsureAdmin()
    {
        if (!currentUser.IsAdmin) throw new ForbiddenException();
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
