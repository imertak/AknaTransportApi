using AknaTransportApi.Business;
using AknaTransportApi.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;

// Başlangıç hataları için geçici logger; host kurulduğunda yapılandırmadan okunan logger ile değiştirilir.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services
        .AddBusiness()
        .AddInfrastructure(builder.Configuration)
        .AddApi(builder.Configuration, builder.Environment);

    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });

    var app = builder.Build();

    app.UseForwardedHeaders();
    app.UseSerilogRequestLogging(o =>
    {
        // Sorgu dizesi ve gövde loglanmaz (token / kişisel veri sızmasını önlemek için).
        o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} => {StatusCode} ({Elapsed:0} ms)";
    });
    app.UseExceptionHandler();

    if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
    {
        app.UseSwagger();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/swagger/v1/swagger.json", "AKNA Transport API v1");
            o.DocumentTitle = "AKNA Transport API";
        });
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        await next();
    });

    app.UseCors(ApiServiceExtensions.CorsPolicy);
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapControllers();
    app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct)
                ? Results.Ok(new { status = "Healthy" })
                : Results.Json(new { status = "Unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable))
        .AllowAnonymous()
        .ExcludeFromDescription();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Uygulama başlatılamadı.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Integration testleri için (WebApplicationFactory).</summary>
public partial class Program;
