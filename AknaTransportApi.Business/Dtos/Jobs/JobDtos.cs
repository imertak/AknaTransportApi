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
    DateTime? UpdatedAt,
    // Duraklardan otomatik üretilen güzergâh metni (önce yüklemeler, sonra teslimatlar).
    string RouteDescription,
    IReadOnlyList<JobStopDto> Stops,
    Guid? AssignedVehicleId,
    string? AssignedVehiclePlate,
    // Fatura durumundan türetilir: NotInvoiced, InDraft, Invoiced.
    JobBillingStatus BillingStatus);

/// <summary>İşin yükleme veya teslimat noktası.</summary>
public sealed record JobStopDto(Guid Id, StopType StopType, int Sequence, string Address, double? Latitude, double? Longitude, string? Description);

public sealed record JobStatusHistoryDto(
    Guid Id,
    JobStatus? FromStatus,
    JobStatus ToStatus,
    Guid ChangedByUserId,
    string? ChangedByUserName,
    DateTime ChangedAt,
    string? Description);

public sealed record JobDetailDto(JobDto Job, IReadOnlyList<JobStatusHistoryDto> StatusHistory);

public sealed class JobStopRequest
{
    public StopType StopType { get; set; }
    public string Address { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Description { get; set; }
}

public class JobRequestBase
{
    public DateOnly JobDate { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public VehicleType VehicleType { get; set; }

    /// <summary>
    /// Yükleme ve teslimat noktaları; her tip kendi içinde gönderildiği sırayla saklanır.
    /// En az bir yükleme ve bir teslimat noktası gerekir.
    /// </summary>
    public List<JobStopRequest>? Stops { get; set; }

    /// <summary>Eski istemciler için tek yükleme adresi. Stops gönderilirse yok sayılır.</summary>
    public string? PickupAddress { get; set; }
    /// <summary>Eski istemciler için tek teslimat adresi. Stops gönderilirse yok sayılır.</summary>
    public string? DeliveryAddress { get; set; }
    public double? PickupLatitude { get; set; }
    public double? PickupLongitude { get; set; }
    public double? DeliveryLatitude { get; set; }
    public double? DeliveryLongitude { get; set; }

    /// <summary>Elle yazılan iş açıklaması (isteğe bağlı). Güzergâh metni duraklardan ayrıca üretilir.</summary>
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? Notes { get; set; }

    /// <summary>Stops veya eski tekli adres alanlarından durak listesini üretir.</summary>
    public IReadOnlyList<JobStopInput> ResolveStops()
    {
        if (Stops is { Count: > 0 })
            return Stops.Select(s => new JobStopInput(s.StopType, s.Address, s.Latitude, s.Longitude, s.Description)).ToList();
        var list = new List<JobStopInput>();
        if (!string.IsNullOrWhiteSpace(PickupAddress)) list.Add(new JobStopInput(StopType.Pickup, PickupAddress, PickupLatitude, PickupLongitude, null));
        if (!string.IsNullOrWhiteSpace(DeliveryAddress)) list.Add(new JobStopInput(StopType.Delivery, DeliveryAddress, DeliveryLatitude, DeliveryLongitude, null));
        return list;
    }
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

    /// <summary>İşte kullanılacak araç. Seçilen şoförün (veya assignToMe ise yöneticinin) aktif aracı olmalıdır.</summary>
    public Guid? AssignedVehicleId { get; set; }

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

    /// <summary>İşte kullanılacak araç; şoförün aktif araçlarından biri olmalıdır.</summary>
    public Guid? VehicleId { get; set; }
    public string? Note { get; set; }
}

/// <summary>Yöneticinin işi üstlenmesi; isteğe bağlı olarak kendi araçlarından biri seçilir.</summary>
public sealed class ClaimJobRequest
{
    public Guid? VehicleId { get; set; }
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
    /// <summary>Faturalandırma durumu: NotInvoiced, InDraft, Invoiced.</summary>
    public JobBillingStatus? BillingStatus { get; set; }
}
