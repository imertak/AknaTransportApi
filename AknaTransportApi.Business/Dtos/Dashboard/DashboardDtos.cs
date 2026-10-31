using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Dtos.Dashboard;

public sealed record DashboardSummaryDto(
    int TotalJobs,
    int PendingJobs,
    int AssignedJobs,
    // Kabul edilmiş, yolda, yüklemede ve teslimatta olan işler.
    int ActiveJobs,
    int CompletedJobs,
    int CancelledJobs,
    // Tamamlanan işlerin toplam nakliye tutarı.
    decimal TotalRevenue,
    // Bu ay tamamlanan işlerin toplam tutarı (iş tarihine göre).
    decimal MonthlyRevenue,
    int TotalCustomers,
    // Şu anda aktif bir işi bulunan şoför sayısı.
    int ActiveDrivers,
    DateOnly Today);

public sealed record DailyJobCountDto(DateOnly Date, int Total, int Completed, decimal CompletedAmount);

public sealed record MonthlyRevenueDto(int Year, int Month, decimal Revenue, int CompletedJobs);

public sealed record ActiveDriverDto(
    Guid DriverId,
    string FullName,
    string PhoneNumber,
    UserRole Role,
    Guid JobId,
    string JobNumber,
    JobStatus JobStatus,
    string StoreName,
    LocationDto? LastLocation);
