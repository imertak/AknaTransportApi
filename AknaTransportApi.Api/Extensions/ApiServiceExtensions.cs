using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace AknaTransportApi.Api.Extensions;

public static class ApiServiceExtensions
{
    public const string CorsPolicy = "MobileApp";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddControllers(options =>
            {
                options.Filters.Add<PasswordChangeFilter>();
                options.Filters.Add<ValidationFilter>();
            })
            .AddJsonOptions(o =>
            {
                // Enum değerleri İngilizce ve sabit string olarak döner (ör. "OnTheWay"); frontend Türkçe karşılığa çevirir.
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            })
            .ConfigureApiBehaviorOptions(o =>
            {
                o.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(e => e.Value?.Errors.Count > 0)
                        .SelectMany(e => e.Value!.Errors.Select(err => new ErrorDetail(
                            string.IsNullOrEmpty(e.Key) ? null : e.Key.TrimStart('$', '.'),
                            string.IsNullOrWhiteSpace(err.ErrorMessage) ? "Geçersiz değer." : err.ErrorMessage)))
                        .ToList();
                    return new BadRequestObjectResult(BaseResponse.Fail("İstek doğrulanamadı.", errors));
                };
            });

        services.AddJwtAuthentication();
        services.AddRateLimiting();
        services.AddSwagger();

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
        {
            // Native mobil uygulama CORS'a tabi değildir; liste Expo web / yönetim paneli içindir.
            if (origins.Length > 0) p.WithOrigins(origins);
            else if (environment.IsDevelopment()) p.AllowAnyOrigin();
            p.AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Content-Disposition");
        }));

        return services;
    }

    private static void AddJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        // Ayarlar çözümleme anında okunur (environment variable / test yapılandırması dahil).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<Microsoft.Extensions.Options.IOptions<JwtOptions>>((o, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.NameIdentifier,
                };
                o.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        var expired = context.AuthenticateFailure is SecurityTokenExpiredException;
                        if (expired) context.Response.Headers["X-Token-Expired"] = "true";
                        await context.Response.WriteAsJsonAsync(BaseResponse.Fail(expired
                            ? "Oturum süresi doldu. Token'ı yenileyin."
                            : "Bu işlem için giriş yapmalısınız."));
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(BaseResponse.Fail("Bu işlem için yetkiniz yok."));
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AdminOnly, p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin)));
    }

    private static void AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var limits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(BaseResponse.Fail("Çok fazla istek gönderildi. Lütfen biraz bekleyin."), ct);
            };

            // Genel sınır: kullanıcı (yoksa IP) başına dakikada N istek.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.GlobalPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));

            // Giriş, şifre sıfırlama: IP başına (kaba kuvvet koruması).
            options.AddPolicy(RateLimitPolicies.Auth, ctx => RateLimitPartition.GetFixedWindowLimiter(
                "ip:" + (ctx.Connection.RemoteIpAddress ?? IPAddress.Loopback),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.AuthPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // Konum: kullanıcı başına (cihaz ~20 sn'de bir gönderir).
            options.AddPolicy(RateLimitPolicies.Location, ctx => RateLimitPartition.GetSlidingWindowLimiter(
                PartitionKey(ctx),
                _ => new SlidingWindowRateLimiterOptions { PermitLimit = limits.LocationPerMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 }));

            options.AddPolicy(RateLimitPolicies.PublicTracking, ctx => RateLimitPartition.GetFixedWindowLimiter(
                "ip:" + (ctx.Connection.RemoteIpAddress ?? IPAddress.Loopback),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.PublicTrackingPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    private static string PartitionKey(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
            ? "user:" + userId
            : "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static void AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "AKNA Transport API",
                Version = "v1",
                Description = "Nakliye ve lojistik yönetim sistemi. Tüm yanıtlar BaseResponse zarfındadır; tarihler UTC (ISO 8601), iş/fatura tarihleri yyyy-MM-dd.",
            });
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "POST /api/auth/login yanıtındaki accessToken değerini girin.",
            });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
            });
            o.SupportNonNullableReferenceTypes();
            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml)) o.IncludeXmlComments(xml);
        });
    }
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public int GlobalPerMinute { get; set; } = 300;
    public int AuthPerMinute { get; set; } = 10;
    public int LocationPerMinute { get; set; } = 30;
    public int PublicTrackingPerMinute { get; set; } = 60;
}
