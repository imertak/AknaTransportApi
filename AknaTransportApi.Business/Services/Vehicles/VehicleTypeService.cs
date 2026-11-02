using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Services.Vehicles;

/// <summary>Yöneticinin tanımladığı araç tipleri. Tipler silinmez; kullanımdan kalkan tip pasife alınır.</summary>
public sealed class VehicleTypeService(
    IRepository<VehicleTypeDefinition> types,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IVehicleTypeService
{
    public const int CodeMaxLength = 50;

    public async Task<IReadOnlyList<VehicleTypeDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = types.QueryAsNoTracking();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        return await q.OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new VehicleTypeDto(t.Id, t.Code, t.Name, t.SortOrder, t.IsActive)).ToListAsync(ct);
    }

    public async Task<VehicleTypeDto> CreateAsync(VehicleTypeRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var name = request.Name.Trim();
        await EnsureNameAvailableAsync(name, null, ct);

        var baseCode = ToCode(name);
        var code = baseCode;
        for (var i = 2; await types.QueryAsNoTracking().AnyAsync(t => t.Code == code, ct); i++)
            code = $"{baseCode[..Math.Min(baseCode.Length, CodeMaxLength - 4)]}{i}";

        var sort = request.SortOrder ?? (await types.QueryAsNoTracking().MaxAsync(t => (int?)t.SortOrder, ct) ?? 0) + 10;
        var type = new VehicleTypeDefinition { Code = code, Name = name, SortOrder = sort, IsActive = true };
        types.Add(type);
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(type);
    }

    public async Task<VehicleTypeDto> UpdateAsync(Guid id, VehicleTypeRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var type = await types.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Araç tipi");
        var name = request.Name.Trim();
        await EnsureNameAvailableAsync(name, id, ct);
        // Kod değişmez: mevcut iş ve araç kayıtları koda bağlıdır, yeni ad hepsinde görünür.
        type.Name = name;
        if (request.SortOrder is { } sort) type.SortOrder = sort;
        type.IsActive = request.IsActive;
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(type);
    }

    public async Task EnsureSelectableAsync(string code, string? currentCode, CancellationToken ct)
    {
        if (string.Equals(code, currentCode, StringComparison.Ordinal)) return;
        if (!await types.QueryAsNoTracking().AnyAsync(t => t.Code == code && t.IsActive, ct))
            throw new BusinessValidationException("Seçilen araç tipi bulunamadı veya pasif durumda.", [new ErrorDetail("vehicleType", "Geçerli bir araç tipi seçin.")]);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(CancellationToken ct) =>
        await types.QueryAsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Name, ct);

    private async Task EnsureNameAvailableAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await types.QueryAsNoTracking().AnyAsync(t => t.Name.ToLower() == lower && t.Id != exceptId, ct))
            throw new ConflictException($"\"{name}\" adında bir araç tipi zaten var.");
    }

    /// <summary>"Kapalı Kasa Kamyon" → "KapaliKasaKamyon". Türkçe harfler sadeleştirilir, yalnızca harf ve rakam kalır.</summary>
    public static string ToCode(string name)
    {
        var map = new Dictionary<char, char> { ['ı'] = 'i', ['İ'] = 'I', ['ş'] = 's', ['Ş'] = 'S', ['ğ'] = 'g', ['Ğ'] = 'G', ['ç'] = 'c', ['Ç'] = 'C', ['ö'] = 'o', ['Ö'] = 'O', ['ü'] = 'u', ['Ü'] = 'U' };
        var normalized = new string(name.Select(c => map.TryGetValue(c, out var r) ? r : c).ToArray()).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        var upperNext = true;
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }
            else upperNext = true;
        }
        var code = sb.Length == 0 ? "Tip" : sb.ToString();
        if (char.IsDigit(code[0])) code = "Tip" + code;
        return code.Length > CodeMaxLength ? code[..CodeMaxLength] : code;
    }

    private static VehicleTypeDto ToDto(VehicleTypeDefinition t) => new(t.Id, t.Code, t.Name, t.SortOrder, t.IsActive);

    private void EnsureAdmin()
    {
        if (!currentUser.IsAdmin) throw new ForbiddenException();
    }
}
