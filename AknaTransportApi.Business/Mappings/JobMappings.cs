using System.Linq.Expressions;

namespace AknaTransportApi.Business.Mappings;

public static class JobMappings
{
    public static readonly Expression<Func<TransportJob, JobDto>> Projection = j => new JobDto(
        j.Id, j.JobNumber, j.CustomerId, j.Customer!.CompanyName, j.CreatedByUserId, j.AssignedDriverId,
        j.AssignedDriver != null ? j.AssignedDriver.FullName : null,
        j.JobDate, j.StoreName, j.VehicleType, j.PickupAddress, j.DeliveryAddress,
        j.PickupLatitude, j.PickupLongitude, j.DeliveryLatitude, j.DeliveryLongitude,
        j.Description, j.Price, j.Currency, j.Status, j.Notes, j.CancellationReason,
        j.InvoiceId, j.Invoice != null ? j.Invoice.InvoiceNumber : null,
        j.StartedAt, j.CompletedAt, j.CancelledAt, j.CreatedAt, j.UpdatedAt);

    public static readonly Expression<Func<JobStatusHistory, JobStatusHistoryDto>> HistoryProjection = h => new JobStatusHistoryDto(
        h.Id, h.FromStatus, h.ToStatus, h.ChangedByUserId, h.ChangedByUser != null ? h.ChangedByUser.FullName : null, h.ChangedAt, h.Description);

    public static void Apply(this TransportJob job, JobRequestBase request)
    {
        job.JobDate = request.JobDate;
        job.StoreName = request.StoreName.Trim();
        job.VehicleType = request.VehicleType;
        job.PickupAddress = request.PickupAddress.Trim();
        job.DeliveryAddress = request.DeliveryAddress.Trim();
        job.PickupLatitude = request.PickupLatitude;
        job.PickupLongitude = request.PickupLongitude;
        job.DeliveryLatitude = request.DeliveryLatitude;
        job.DeliveryLongitude = request.DeliveryLongitude;
        job.Description = request.Description.Trim();
        job.Price = Domain.Common.Money.Round(request.Price);
        job.Notes = request.Notes.TrimOrNull();
    }
}
