using System.Net;
using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Dashboard;
using AknaTransport.Business.Features.Reports;
using AknaTransport.Tests.Infrastructure;

namespace AknaTransport.Tests.Integration;

public class ReportTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Report_applies_selected_date_range_and_status_scope()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        async Task<Guid> Completed(DateOnly date, decimal price)
        {
            var job = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true, date: date, price: price);
            await _client.RunToCompletionAsync(job.Job.Id, admin);
            return job.Job.Id;
        }

        await Completed(new DateOnly(2026, 10, 18), 1000m);   // aralık dışı (önce)
        await Completed(new DateOnly(2026, 10, 19), 4000m);
        await Completed(new DateOnly(2026, 10, 22), 3000m);
        await Completed(new DateOnly(2026, 10, 23), 9000m);   // aralık dışı (sonra)
        await _client.CreateJobAsync(admin, customer.Id, date: new DateOnly(2026, 10, 20), price: 500m); // tamamlanmamış

        var url = $"/api/reports/jobs?startDate=2026-10-19&endDate=2026-10-22&customerId={customer.Id}";
        var report = await _client.OkAsync<JobReportDto>(HttpMethod.Get, url, token: admin);
        Assert.Equal(2, report.TotalCount);
        Assert.Equal(7000m, report.TotalAmount);
        Assert.All(report.Jobs.Items, j => Assert.InRange(j.JobDate, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 22)));

        var all = await _client.OkAsync<JobReportDto>(HttpMethod.Get, url + "&scope=All", token: admin);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(7500m, all.TotalAmount);

        var summary = await _client.OkAsync<JobReportSummaryDto>(HttpMethod.Get, $"/api/reports/jobs/summary?startDate=2026-10-19&endDate=2026-10-22&customerId={customer.Id}", token: admin);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(2, summary.ByDay.Count);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/reports/jobs/pdf?startDate=2026-10-19&endDate=2026-10-22&customerId={customer.Id}");
        request.Headers.Authorization = new("Bearer", admin);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Contains("20261019-20261022", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
    }

    [Fact]
    public async Task Invalid_date_range_is_rejected()
    {
        var admin = await _client.AdminTokenAsync();
        var (status, _) = await _client.SendAsync<JobReportDto>(HttpMethod.Get, "/api/reports/jobs?startDate=2026-10-22&endDate=2026-10-19", token: admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Reports_and_dashboard_are_admin_only()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        foreach (var url in new[] { "/api/reports/jobs", "/api/reports/jobs/pdf", "/api/dashboard/summary", "/api/invoices", "/api/customers" })
        {
            var (status, _) = await _client.SendAsync<object>(HttpMethod.Get, url, token: driver.Token);
            Assert.Equal(HttpStatusCode.Forbidden, status);
        }
    }

    [Fact]
    public async Task Dashboard_summary_is_calculated()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true, date: new DateOnly(2026, 10, 21), price: 2500m);
        await _client.RunToCompletionAsync(job.Job.Id, admin);

        var summary = await _client.OkAsync<DashboardSummaryDto>(HttpMethod.Get, "/api/dashboard/summary", token: admin);
        Assert.True(summary.TotalJobs >= 1);
        Assert.True(summary.CompletedJobs >= 1);
        Assert.True(summary.TotalRevenue >= 2500m);
        Assert.True(summary.TotalCustomers >= 1);

        var daily = await _client.OkAsync<List<DailyJobCountDto>>(HttpMethod.Get, "/api/dashboard/daily-jobs?days=14", token: admin);
        Assert.Equal(14, daily.Count);
        var monthly = await _client.OkAsync<List<MonthlyRevenueDto>>(HttpMethod.Get, "/api/dashboard/monthly-revenue?months=6", token: admin);
        Assert.Equal(6, monthly.Count);
        await _client.OkAsync<object>(HttpMethod.Get, "/api/dashboard/recent-jobs", token: admin);
        await _client.OkAsync<object>(HttpMethod.Get, "/api/dashboard/active-drivers", token: admin);
    }

    [Fact]
    public async Task Customer_with_history_is_deactivated_instead_of_deleted()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        await _client.CreateJobAsync(admin, customer.Id);
        var result = await _client.OkAsync<AknaTransport.Business.Features.Customers.CustomerDeleteResult>(HttpMethod.Delete, $"/api/customers/{customer.Id}", token: admin);
        Assert.False(result.Deleted);
        Assert.True(result.Deactivated);

        var empty = await _client.CreateCustomerAsync(admin);
        var deleted = await _client.OkAsync<AknaTransport.Business.Features.Customers.CustomerDeleteResult>(HttpMethod.Delete, $"/api/customers/{empty.Id}", token: admin);
        Assert.True(deleted.Deleted);

        var summary = await _client.OkAsync<AknaTransport.Business.Features.Customers.CustomerSummaryDto>(HttpMethod.Get, $"/api/customers/{customer.Id}/summary", token: admin);
        Assert.Equal(1, summary.TotalJobs);
        Assert.False(summary.Customer.IsActive);
    }
}
