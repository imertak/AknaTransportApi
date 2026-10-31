using System.Linq.Expressions;

namespace AknaTransportApi.Business.Mappings;

public static class VehicleMappings
{
    public static readonly Expression<Func<Vehicle, VehicleDto>> Projection = v => new VehicleDto(
        v.Id, v.PlateNumber, v.Brand, v.Model, v.VehicleType, v.IsActive, v.Description,
        v.DriverAssignments.Where(a => a.IsActive).Select(a => (Guid?)a.DriverId).FirstOrDefault(),
        v.DriverAssignments.Where(a => a.IsActive).Select(a => a.Driver!.FullName).FirstOrDefault(),
        v.DriverAssignments.Where(a => a.IsActive).Select(a => (DateTime?)a.AssignedAt).FirstOrDefault(),
        v.CreatedAt, v.UpdatedAt);

    public static void Apply(this Vehicle vehicle, VehicleRequest request)
    {
        vehicle.PlateNumber = Vehicle.NormalizePlate(request.PlateNumber);
        vehicle.Brand = request.Brand.TrimOrNull();
        vehicle.Model = request.Model.TrimOrNull();
        vehicle.VehicleType = request.VehicleType;
        vehicle.Description = request.Description.TrimOrNull();
    }
}
