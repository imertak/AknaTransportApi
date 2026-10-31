using AknaTransport.Business.Features.Auth;
using AknaTransport.Business.Features.Customers;
using AknaTransport.Business.Features.Dashboard;
using AknaTransport.Business.Features.Invoices;
using AknaTransport.Business.Features.Jobs;
using AknaTransport.Business.Features.Locations;
using AknaTransport.Business.Features.Outbox;
using AknaTransport.Business.Features.Reports;
using AknaTransport.Business.Features.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AknaTransport.Business;

public static class DependencyInjection
{
    public static IServiceCollection AddBusiness(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IBusinessCalendar, BusinessCalendar>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IOutboxProcessor, OutboxProcessor>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: false);
        return services;
    }
}
