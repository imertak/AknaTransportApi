
namespace AknaTransportApi.Business.Tests.Unit;

public class TransportJobTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Driver = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private static TransportJob NewJob(Guid? driver = null)
    {
        var job = new TransportJob { JobNumber = "AKN-2026-000001", Price = 4000m };
        job.Initialize(Admin, driver, Now);
        return job;
    }

    [Fact]
    public void Full_flow_records_every_status_change()
    {
        var job = NewJob(Driver);
        job.Accept(Driver, Now);
        job.Start(Driver, Now);
        job.StartLoading(Driver, Now);
        job.StartDelivery(Driver, Now);
        job.Complete(Driver, Now);

        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.NotNull(job.StartedAt);
        Assert.NotNull(job.CompletedAt);
        Assert.Equal(6, job.StatusHistory.Count);
        Assert.Equal(JobStatus.Delivering, job.StatusHistory.Last().FromStatus);
        Assert.All(job.StatusHistory.Skip(1), h => Assert.Equal(Driver, h.ChangedByUserId));
    }

    [Fact]
    public void Completed_job_cannot_be_started_again()
    {
        var job = NewJob(Driver);
        job.Accept(Driver, Now);
        job.Start(Driver, Now);
        job.StartLoading(Driver, Now);
        job.StartDelivery(Driver, Now);
        job.Complete(Driver, Now);

        Assert.Throws<InvalidJobStatusTransitionException>(() => job.Start(Driver, Now));
        Assert.Throws<InvalidJobStatusTransitionException>(() => job.StartLoading(Driver, Now));
        Assert.Throws<InvalidJobStatusTransitionException>(() => job.Cancel(Admin, Now, null));
        Assert.Throws<InvalidJobStatusTransitionException>(() => job.AssignDriver(Driver, Admin, Now));
    }

    [Fact]
    public void Steps_cannot_be_skipped()
    {
        var job = NewJob(Driver);
        Assert.Throws<InvalidJobStatusTransitionException>(() => job.Start(Driver, Now));
        Assert.Throws<InvalidJobStatusTransitionException>(() => job.Complete(Driver, Now));
    }

    [Fact]
    public void Admin_claim_assigns_to_self_and_accepts()
    {
        var job = NewJob();
        Assert.Equal(JobStatus.Pending, job.Status);
        job.ClaimBy(Admin, Now);
        Assert.Equal(Admin, job.AssignedDriverId);
        Assert.Equal(JobStatus.Accepted, job.Status);
    }

    [Fact]
    public void Unassigning_returns_job_to_pending()
    {
        var job = NewJob(Driver);
        job.AssignDriver(null, Admin, Now);
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Null(job.AssignedDriverId);
    }

    [Fact]
    public void Invoiced_job_cannot_be_edited_or_deleted()
    {
        var job = NewJob(Driver);
        job.InvoiceId = Guid.NewGuid();
        Assert.Throws<DomainException>(job.EnsureEditable);
        Assert.Throws<DomainException>(job.EnsureDeletable);
    }
}

public class InvoiceTests
{
    [Fact]
    public void Totals_are_calculated_with_decimal_rounding()
    {
        var invoice = new Invoice();
        invoice.Items.Add(new InvoiceItem { Quantity = 1, UnitPrice = 4000m });
        invoice.Items.Add(new InvoiceItem { Quantity = 1, UnitPrice = 3500.55m });
        invoice.Recalculate(20m);

        Assert.Equal(7500.55m, invoice.SubTotal);
        Assert.Equal(1500.11m, invoice.VatAmount);
        Assert.Equal(9000.66m, invoice.GrandTotal);
        Assert.All(invoice.Items, i => Assert.Equal(20m, i.VatRate));
    }

    [Fact]
    public void Draft_invoice_cannot_be_marked_issued_without_provider()
    {
        var invoice = new Invoice();
        Assert.Throws<InvalidInvoiceStateException>(() => invoice.MarkIssued("ref", null, null, DateTime.UtcNow));

        invoice.MarkPending("Provider");
        Assert.Throws<InvalidInvoiceStateException>(() => invoice.MarkIssued("", null, null, DateTime.UtcNow));
        invoice.MarkIssued("ETTN-1", "AKN2026000000001", null, DateTime.UtcNow);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.False(invoice.IsEditable);
    }

    [Fact]
    public void Vat_is_rounded_half_away_from_zero()
    {
        Assert.Equal(0.01m, Money.CalculateVat(0.05m, 10m)); // 0.005 -> 0.01
        Assert.Equal(7300m, Money.CalculateVat(36500m, 20m));
    }
}

public class GeoTests
{
    [Fact]
    public void Distance_between_known_points_is_reasonable()
    {
        // Haliç Tersane - Gebze yaklaşık 50 km
        var d = Geo.DistanceMeters(41.0383, 28.9469, 40.8026, 29.4307);
        Assert.InRange(d, 45_000, 55_000);
        Assert.Equal(0, Geo.DistanceMeters(41, 29, 41, 29), 3);
    }
}
