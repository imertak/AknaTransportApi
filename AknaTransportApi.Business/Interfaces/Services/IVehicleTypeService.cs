namespace AknaTransportApi.Business.Interfaces.Services;

public interface IVehicleTypeService
{
    /// <summary>Araç tipleri. Pasifler yalnızca <paramref name="includeInactive"/> ile gelir (eski kayıtların adını göstermek için).</summary>
    Task<IReadOnlyList<VehicleTypeDto>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<VehicleTypeDto> CreateAsync(VehicleTypeRequest request, CancellationToken ct);
    Task<VehicleTypeDto> UpdateAsync(Guid id, VehicleTypeRequest request, CancellationToken ct);

    /// <summary>
    /// Yeni kayıtta seçilen tipin var ve aktif olduğunu doğrular. Kayıt zaten bu tipteyse (<paramref name="currentCode"/>)
    /// tip pasife alınmış olsa da kabul edilir; böylece eski işler düzenlenebilir.
    /// </summary>
    Task EnsureSelectableAsync(string code, string? currentCode, CancellationToken ct);

    /// <summary>Kod → ad sözlüğü (PDF ve raporlar için). Tanımı olmayan kod kendi adıyla gösterilir.</summary>
    Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(CancellationToken ct);
}
