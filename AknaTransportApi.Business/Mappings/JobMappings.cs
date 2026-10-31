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
        j.StartedAt, j.CompletedAt, j.CancelledAt, j.CreatedAt, j.UpdatedAt,
        j.RouteDescription,
        // StopType veritabanında metin olarak tutulur; alfabetik sıralama "Delivery"yi öne alacağı için açıkça sıralanır.
        j.Stops.OrderBy(s => s.StopType == StopType.Pickup ? 0 : 1).ThenBy(s => s.Sequence)
            .Select(s => new JobStopDto(s.Id, s.StopType, s.Sequence, s.Address, s.Latitude, s.Longitude, s.Description)).ToList(),
        j.AssignedVehicleId,
        j.AssignedVehicle != null ? j.AssignedVehicle.PlateNumber : null,
        j.InvoiceId == null ? JobBillingStatus.NotInvoiced
            : j.Invoice!.Status == InvoiceStatus.Issued ? JobBillingStatus.Invoiced : JobBillingStatus.InDraft);

    /// <summary>PDF ve fatura satırları için iş metni: otomatik güzergâh + elle yazılan açıklama.</summary>
    public static string ComposeText(string routeDescription, string description) =>
        string.IsNullOrWhiteSpace(routeDescription) ? description
        : string.IsNullOrWhiteSpace(description) ? routeDescription
        : $"{routeDescription} ({description})";

    public static readonly Expression<Func<JobStatusHistory, JobStatusHistoryDto>> HistoryProjection = h => new JobStatusHistoryDto(
        h.Id, h.FromStatus, h.ToStatus, h.ChangedByUserId, h.ChangedByUser != null ? h.ChangedByUser.FullName : null, h.ChangedAt, h.Description);

    public static void Apply(this TransportJob job, JobRequestBase request)
    {
        job.JobDate = request.JobDate;
        job.StoreName = request.StoreName.Trim();
        job.VehicleType = request.VehicleType;
        job.Description = request.Description.TrimOrNull() ?? string.Empty;
        job.Price = Domain.Common.Money.Round(request.Price);
        job.Notes = request.Notes.TrimOrNull();
    }
}
