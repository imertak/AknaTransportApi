
namespace AknaTransportApi.Domain.Entities;

public class Customer : Entity, IHasTimestamps
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? PhoneNumber { get; set; }
    /// <summary>Ana e-posta adresi. Fatura e-postası bu adrese, diğer adresler bilgi (CC) olarak gider.</summary>
    public string? Email { get; set; }
    /// <summary>Ek e-posta adresleri, ";" ile ayrılmış. Boşsa yalnızca <see cref="Email"/> kullanılır.</summary>
    public string? AdditionalEmails { get; set; }
    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<TransportJob> Jobs { get; set; } = new List<TransportJob>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public const char EmailSeparator = ';';

    /// <summary>Ana adres önce olmak üzere tüm e-posta adresleri (boşlar ve tekrarlar atılır).</summary>
    public IReadOnlyList<string> AllEmails() => MergeEmails(Email, AdditionalEmails);

    public static IReadOnlyList<string> MergeEmails(string? primary, string? additional) =>
        new[] { primary }.Concat((additional ?? string.Empty).Split(EmailSeparator))
            .Select(e => e?.Trim())
            .Where(e => !string.IsNullOrEmpty(e))
            .Select(e => e!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
