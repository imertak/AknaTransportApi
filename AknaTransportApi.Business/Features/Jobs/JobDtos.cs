using System.Linq.Expressions;
using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Customers;
using AknaTransport.Domain.Entities;
using AknaTransport.Domain.Enums;

namespace AknaTransport.Business.Features.Jobs;

public sealed record JobDto(
    Guid Id,
    string JobNumber,
    Guid CustomerId,
    string CustomerName,
    Guid CreatedByUserId,
    Guid? AssignedDriverId,
    string? AssignedDriverName,
    DateOnly JobDate,
    string StoreName,
    VehicleType VehicleType,
    string PickupAddress,
    string DeliveryAddress,
    double? PickupLatitude,
    double? PickupLongitude,
    double? DeliveryLatitude,
    double? DeliveryLongitude,
    string Description,
    decimal Price,
    string Currency,
    JobStatus Status,
    string? Notes,
    string? CancellationReason,
    Guid? InvoiceId,
    string? InvoiceNumber,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record JobStatusHistoryDto(
    Guid Id,
    JobStatus? FromStatus,
    JobStatus ToStatus,
    Guid ChangedByUserId,
    string? ChangedByUserName,
    DateTime ChangedAt,
    string? Description);

public sealed record JobDetailDto(JobDto Job, IReadOnlyList<JobStatusHistoryDto> StatusHistory);

public class JobRequestBase
{
    public DateOnly JobDate { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public VehicleType VehicleType { get; set; }
    public string PickupAddress { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public double? PickupLatitude { get; set; }
    public double? PickupLongitude { get; set; }
    public double? DeliveryLatitude { get; set; }
    public double? DeliveryLongitude { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? Notes { get; set; }
}

public sealed class CreateJobRequest : JobRequestBase
{
    /// <summary>Mevcut müşteri. NewCustomer verilmişse boş bırakılır.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>İşle birlikte yeni müşteri oluşturmak için.</summary>
    public CustomerRequest? NewCustomer { get; set; }

    /// <summary>Atanacak şoför (User veya Admin).</summary>
    public Guid? AssignedDriverId { get; set; }

    /// <summary>true ise iş, oluşturan yöneticiye atanır.</summary>
    public bool AssignToMe { get; set; }

    /// <summary>Varsayılan TRY.</summary>
    public string? Currency { get; set; }
}

public sealed class UpdateJobRequest : JobRequestBase
{
    public Guid CustomerId { get; set; }
}

public sealed class AssignJobRequest
{
    /// <summary>null ise atama kaldırılır ve iş Pending durumuna döner.</summary>
    public Guid? DriverId { get; set; }
    public string? Note { get; set; }
}

public sealed class JobActionRequest
{
    public string? Note { get; set; }
}

public sealed class CancelJobRequest
{
    public string? Reason { get; set; }
}

public sealed class JobQuery : PagedQuery
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? DriverId { get; set; }
    /// <summary>Birden fazla durum için ?status=Pending&amp;status=Assigned</summary>
    public List<JobStatus>? Status { get; set; }
    public VehicleType? VehicleType { get; set; }
    public string? StoreName { get; set; }
    /// <summary>İş numarası, mağaza, adres veya açıklamada arama.</summary>
    public string? Search { get; set; }
    /// <summary>true: faturalanmamış, false: faturalanmış işler.</summary>
    public bool? Uninvoiced { get; set; }
}

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

/// <summary>Araç tiplerinin Türkçe karşılıkları (PDF ve e-posta için).</summary>
public static class VehicleTypeLabels
{
    public static string ToTurkish(this VehicleType type) => type switch
    {
        VehicleType.Panelvan => "Panelvan",
        VehicleType.SmallPanelvan => "Küçük Panelvan",
        VehicleType.Pickup => "Kamyonet",
        VehicleType.Truck => "Kamyon",
        _ => "Diğer",
    };
}
