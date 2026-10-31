using System.Linq.Expressions;

namespace AknaTransportApi.Business.Dtos.Jobs;

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
