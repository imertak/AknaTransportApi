using AknaTransport.Domain.Common;
using AknaTransport.Domain.Enums;

namespace AknaTransport.Domain.Entities;

public class JobStatusHistory : Entity
{
    public Guid JobId { get; set; }
    public TransportJob? Job { get; set; }
    /// <summary>İlk oluşturma kaydında null'dır.</summary>
    public JobStatus? FromStatus { get; set; }
    public JobStatus ToStatus { get; set; }
    public Guid ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Description { get; set; }
}
