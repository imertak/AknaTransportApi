
namespace AknaTransportApi.Domain.Entities;

public class Customer : Entity, IHasTimestamps
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<TransportJob> Jobs { get; set; } = new List<TransportJob>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
