
namespace AknaTransportApi.Domain.Entities;

/// <summary>
/// Nakliye işi. Durum geçişleri yalnızca bu sınıftaki metotlarla yapılır;
/// her geçiş <see cref="JobStatusHistory"/> kaydı üretir.
/// </summary>
public class TransportJob : Entity, IHasTimestamps
{
    /// <summary>
    /// Konum paylaşımının kabul edildiği durumlar. Takip, şoför işi kabul ettiği anda başlar ve
    /// iş tamamlanana veya iptal edilene kadar sürer.
    /// </summary>
    public static readonly IReadOnlyList<JobStatus> TrackableStatuses = [JobStatus.Accepted, JobStatus.OnTheWay, JobStatus.Loading, JobStatus.Delivering];

    public const int RouteDescriptionMaxLength = 2000;
    public const string RouteSeparator = " - ";

    /// <summary>Şoförün sahada yürüttüğü durumlar.</summary>
    public static readonly IReadOnlyList<JobStatus> InProgressStatuses = [JobStatus.Accepted, JobStatus.OnTheWay, JobStatus.Loading, JobStatus.Delivering];

    /// <summary>Şoför ataması değiştirilebilen (iş henüz başlamamış) durumlar.</summary>
    public static readonly IReadOnlyList<JobStatus> AssignableStatuses = [JobStatus.Pending, JobStatus.Assigned, JobStatus.Accepted];

    public string JobNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public Guid? AssignedDriverId { get; set; }
    public User? AssignedDriver { get; set; }

    /// <summary>İşte kullanılacak araç. Yalnızca atanan şoförün aktif araçlarından biri olabilir.</summary>
    public Guid? AssignedVehicleId { get; private set; }
    public Vehicle? AssignedVehicle { get; set; }

    public DateOnly JobDate { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public VehicleType VehicleType { get; set; }
    /// <summary>İlk yükleme noktası (liste, arama ve harita için özet). Duraklardan otomatik doldurulur.</summary>
    public string PickupAddress { get; private set; } = string.Empty;
    /// <summary>Son teslimat noktası (liste, arama ve müşteri takibi için özet). Duraklardan otomatik doldurulur.</summary>
    public string DeliveryAddress { get; private set; } = string.Empty;
    public double? PickupLatitude { get; private set; }
    public double? PickupLongitude { get; private set; }
    public double? DeliveryLatitude { get; private set; }
    public double? DeliveryLongitude { get; private set; }

    /// <summary>
    /// Duraklardan otomatik üretilen güzergâh metni: önce tüm yükleme, sonra tüm teslimat noktaları
    /// ("İstanbul / Ümraniye - İstanbul / İkitelli - İstanbul / Zorlu AVM"). Duraklar değişince yeniden üretilir.
    /// </summary>
    public string RouteDescription { get; private set; } = string.Empty;

    /// <summary>Kullanıcının elle yazdığı açıklama. Durak değişikliklerinden etkilenmez.</summary>
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = Money.DefaultCurrency;
    public JobStatus Status { get; private set; } = JobStatus.Pending;
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>İşin bağlı olduğu aktif fatura. Bir iş aynı anda yalnızca bir faturada bulunabilir.</summary>
    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>İyimser eşzamanlılık kontrolü: aynı işin aynı anda iki kez güncellenmesini engeller.</summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public ICollection<JobStatusHistory> StatusHistory { get; set; } = new List<JobStatusHistory>();

    /// <summary>Yükleme ve teslimat noktaları. Yalnızca <see cref="SetStops"/> ile değiştirilir.</summary>
    public ICollection<TransportJobStop> Stops { get; set; } = new List<TransportJobStop>();

    public bool IsClosed => Status is JobStatus.Completed or JobStatus.Cancelled;
    public bool IsTrackable => TrackableStatuses.Contains(Status);

    /// <summary>Yeni iş oluşturur ve ilk durum kaydını ekler.</summary>
    public void Initialize(Guid actorId, Guid? driverId, DateTime now, Guid? vehicleId = null)
    {
        if (driverId is null && vehicleId is not null) throw new DomainException("Araç seçmek için önce şoför seçilmelidir.");
        Status = driverId is null ? JobStatus.Pending : JobStatus.Assigned;
        AssignedDriverId = driverId;
        AssignedVehicleId = vehicleId;
        Record(null, Status, actorId, now, driverId is null ? "İş oluşturuldu." : "İş oluşturuldu ve şoföre atandı.");
    }

    /// <summary>Şoför atar veya atamayı kaldırır (driverId null). İş başladıktan sonra atama değişmez.</summary>
    public void AssignDriver(Guid? driverId, Guid actorId, DateTime now, string? note = null, Guid? vehicleId = null)
    {
        EnsureIn(AssignableStatuses, "şoför atama");
        if (driverId is null && vehicleId is not null) throw new DomainException("Araç seçmek için önce şoför seçilmelidir.");
        if (driverId == AssignedDriverId && Status != JobStatus.Pending)
        {
            // Aynı şoför: yalnızca araç değişmiş olabilir; durum geçmişine kayıt düşülmez.
            if (vehicleId != AssignedVehicleId)
            {
                AssignedVehicleId = vehicleId;
                ConcurrencyStamp = Guid.NewGuid();
            }
            return;
        }

        var from = Status;
        AssignedDriverId = driverId;
        AssignedVehicleId = vehicleId;
        Status = driverId is null ? JobStatus.Pending : JobStatus.Assigned;
        Record(from, Status, actorId, now, note ?? (driverId is null ? "Şoför ataması kaldırıldı." : "Şoför atandı."));
    }

    /// <summary>Admin işi kendi üzerine alır; iş kabul edilmiş sayılır.</summary>
    public void ClaimBy(Guid adminId, DateTime now, string? note = null, Guid? vehicleId = null)
    {
        EnsureIn(AssignableStatuses, "üstlenme");
        var from = Status;
        AssignedDriverId = adminId;
        AssignedVehicleId = vehicleId;
        Status = JobStatus.Accepted;
        Record(from, Status, adminId, now, note ?? "İş yönetici tarafından üstlenildi.");
    }

    public void Accept(Guid actorId, DateTime now, string? note = null)
        => Transition(JobStatus.Assigned, JobStatus.Accepted, actorId, now, note ?? "İş kabul edildi.", "kabul etme");

    public void Start(Guid actorId, DateTime now, string? note = null)
    {
        Transition(JobStatus.Accepted, JobStatus.OnTheWay, actorId, now, note ?? "Şoför yola çıktı.", "yola çıkma");
        StartedAt = now;
    }

    public void StartLoading(Guid actorId, DateTime now, string? note = null)
        => Transition(JobStatus.OnTheWay, JobStatus.Loading, actorId, now, note ?? "Yükleme başladı.", "yüklemeye başlama");

    public void StartDelivery(Guid actorId, DateTime now, string? note = null)
        => Transition(JobStatus.Loading, JobStatus.Delivering, actorId, now, note ?? "Teslimata çıkıldı.", "teslimata başlama");

    public void Complete(Guid actorId, DateTime now, string? note = null)
    {
        Transition(JobStatus.Delivering, JobStatus.Completed, actorId, now, note ?? "İş tamamlandı.", "tamamlama");
        CompletedAt = now;
    }

    public void Cancel(Guid actorId, DateTime now, string? reason)
    {
        if (IsClosed) throw new InvalidJobStatusTransitionException(Status, "iptal");
        if (InvoiceId is not null) throw new DomainException("Faturaya eklenmiş iş iptal edilemez.");
        var from = Status;
        Status = JobStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = reason;
        Record(from, Status, actorId, now, string.IsNullOrWhiteSpace(reason) ? "İş iptal edildi." : $"İş iptal edildi: {reason}");
    }

    /// <summary>
    /// Yükleme ve teslimat noktalarını verilen sırayla yeniden yazar; özet adres alanlarını ve
    /// otomatik güzergâh açıklamasını günceller. Elle yazılan <see cref="Description"/> değişmez.
    /// </summary>
    public void SetStops(IReadOnlyList<JobStopInput> stops, DateTime now)
    {
        var pickups = stops.Where(s => s.StopType == StopType.Pickup).ToList();
        var deliveries = stops.Where(s => s.StopType == StopType.Delivery).ToList();
        if (pickups.Count == 0) throw new DomainException("En az bir yükleme adresi girilmelidir.");
        if (deliveries.Count == 0) throw new DomainException("En az bir teslimat adresi girilmelidir.");
        if (stops.Any(s => string.IsNullOrWhiteSpace(s.Address))) throw new DomainException("Durak adresi boş olamaz.");
        if (stops.Any(s => (s.Latitude is null) != (s.Longitude is null)))
            throw new DomainException("Durak konumu için enlem ve boylam birlikte verilmelidir.");

        Stops.Clear();
        AddStops(pickups, StopType.Pickup, now);
        AddStops(deliveries, StopType.Delivery, now);

        var firstPickup = pickups[0];
        var lastDelivery = deliveries[^1];
        PickupAddress = Truncate(firstPickup.Address.Trim(), TransportJobStop.AddressMaxLength);
        PickupLatitude = firstPickup.Latitude;
        PickupLongitude = firstPickup.Longitude;
        DeliveryAddress = Truncate(lastDelivery.Address.Trim(), TransportJobStop.AddressMaxLength);
        DeliveryLatitude = lastDelivery.Latitude;
        DeliveryLongitude = lastDelivery.Longitude;
        RouteDescription = BuildRouteDescription(pickups.Concat(deliveries).Select(s => s.Address));
    }

    /// <summary>Önce yükleme, sonra teslimat adreslerini " - " ile birleştirir.</summary>
    public static string BuildRouteDescription(IEnumerable<string> orderedAddresses) =>
        Truncate(string.Join(RouteSeparator, orderedAddresses.Select(a => a.Trim()).Where(a => a.Length > 0)), RouteDescriptionMaxLength);

    private void AddStops(List<JobStopInput> items, StopType type, DateTime now)
    {
        var sequence = 1;
        foreach (var s in items)
        {
            Stops.Add(new TransportJobStop
            {
                TransportJobId = Id,
                StopType = type,
                Address = Truncate(s.Address.Trim(), TransportJobStop.AddressMaxLength),
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                Sequence = sequence++,
                Description = string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim(),
                CreatedAt = now,
            });
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>İş detayları (adres, fiyat vb.) değiştirilebilir mi?</summary>
    public void EnsureEditable()
    {
        if (Status == JobStatus.Cancelled) throw new DomainException("İptal edilmiş iş düzenlenemez.");
        if (InvoiceId is not null) throw new DomainException("Faturaya eklenmiş iş düzenlenemez. Önce faturayı iptal edin.");
    }

    public void EnsureDeletable()
    {
        if (InvoiceId is not null) throw new DomainException("Faturaya eklenmiş iş silinemez.");
        if (Status is not (JobStatus.Pending or JobStatus.Assigned or JobStatus.Cancelled))
            throw new DomainException("Yalnızca başlamamış veya iptal edilmiş işler silinebilir. Devam eden işleri iptal edin.");
    }

    private void Transition(JobStatus expected, JobStatus next, Guid actorId, DateTime now, string note, string action)
    {
        if (Status != expected) throw new InvalidJobStatusTransitionException(Status, action);
        var from = Status;
        Status = next;
        Record(from, next, actorId, now, note);
    }

    private void EnsureIn(IReadOnlyList<JobStatus> allowed, string action)
    {
        if (!allowed.Contains(Status)) throw new InvalidJobStatusTransitionException(Status, action);
    }

    private void Record(JobStatus? from, JobStatus to, Guid actorId, DateTime now, string? description)
    {
        ConcurrencyStamp = Guid.NewGuid();
        StatusHistory.Add(new JobStatusHistory
        {
            JobId = Id,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = actorId,
            ChangedAt = now,
            Description = description,
        });
    }

    /// <summary>Seed ve test senaryoları için doğrudan durum ataması (geçmiş kaydı oluşturulmaz).</summary>
    public void ForceStatusForSeed(JobStatus status, DateTime? startedAt, DateTime? completedAt)
    {
        Status = status;
        StartedAt = startedAt;
        CompletedAt = completedAt;
    }
}
