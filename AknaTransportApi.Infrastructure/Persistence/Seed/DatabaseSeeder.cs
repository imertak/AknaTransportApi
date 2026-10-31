using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Infrastructure.Persistence.Seed;

public sealed class DatabaseSeeder(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    INumberGenerator numberGenerator,
    IOptions<SeedOptions> options,
    IOptions<DatabaseOptions> databaseOptions,
    IHostEnvironment environment,
    TimeProvider clock,
    ILogger<DatabaseSeeder> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (databaseOptions.Value.ApplyMigrationsOnStartup && db.Database.IsRelational() && db.Database.GetMigrations().Any())
        {
            logger.LogInformation("Bekleyen migration'lar uygulanıyor.");
            await db.Database.MigrateAsync(ct);
        }

        var admin = await EnsureAdminAsync(ct);

        if (environment.IsDevelopment() && options.Value.DemoData)
        {
            await SeedDemoDataAsync(admin, ct);
        }
    }

    private async Task<User?> EnsureAdminAsync(CancellationToken ct)
    {
        var existing = await db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive).OrderBy(u => u.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.AdminEmail) || string.IsNullOrWhiteSpace(opts.AdminPassword))
        {
            logger.LogWarning("Aktif yönetici yok ve Seed:AdminEmail / Seed:AdminPassword tanımlı değil. İlk yönetici oluşturulmadı.");
            return null;
        }

        if (opts.AdminPassword.Length < 10)
            throw new InvalidOperationException("Seed:AdminPassword en az 10 karakter olmalıdır.");

        var admin = new User
        {
            FullName = opts.AdminFullName,
            Email = opts.AdminEmail.Trim(),
            NormalizedEmail = User.NormalizeEmail(opts.AdminEmail),
            PhoneNumber = opts.AdminPhoneNumber,
            PasswordHash = passwordHasher.Hash(opts.AdminPassword),
            Role = UserRole.Admin,
            IsActive = true,
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("İlk yönetici hesabı oluşturuldu. UserId: {UserId}", admin.Id);
        return admin;
    }

    private async Task SeedDemoDataAsync(User? admin, CancellationToken ct)
    {
        if (admin is null || await db.Customers.AnyAsync(ct)) return;
        logger.LogInformation("Development örnek verileri oluşturuluyor.");
        var now = clock.GetUtcNow().UtcDateTime;

        User? driver = null;
        if (!string.IsNullOrWhiteSpace(options.Value.DemoUserPassword))
        {
            var hash = passwordHasher.Hash(options.Value.DemoUserPassword);
            driver = new User { FullName = "Ahmet Yılmaz", Email = "ahmet.sofor@akna.local", NormalizedEmail = "ahmet.sofor@akna.local", PhoneNumber = "+905321112233", PasswordHash = hash, Role = UserRole.User };
            var driver2 = new User { FullName = "Mehmet Demir", Email = "mehmet.sofor@akna.local", NormalizedEmail = "mehmet.sofor@akna.local", PhoneNumber = "+905324445566", PasswordHash = hash, Role = UserRole.User };
            db.Users.AddRange(driver, driver2);
        }
        else
        {
            logger.LogWarning("Seed:DemoUserPassword tanımlı değil; örnek şoför hesapları oluşturulmadı.");
        }

        var starbucks = new Customer { CompanyName = "Starbucks", ContactPerson = "Mağaza Operasyon", Email = "fatura@starbucks.example", PhoneNumber = "+902120000001", TaxOffice = "Boğaziçi Kurumlar", TaxNumber = "1234567890", Address = "İstanbul" };
        var calvin = new Customer { CompanyName = "Calvin Klein", ContactPerson = "Lojistik Birimi", Email = "fatura@calvinklein.example", PhoneNumber = "+902120000002", TaxOffice = "Zincirlikuyu", TaxNumber = "2234567890", Address = "İstanbul" };
        var bestseller = new Customer { CompanyName = "Bestseller", ContactPerson = "Depo Sorumlusu", Email = "fatura@bestseller.example", PhoneNumber = "+902120000003", TaxOffice = "Beylikdüzü", TaxNumber = "3234567890", Address = "İstanbul" };
        db.Customers.AddRange(starbucks, calvin, bestseller);

        var van = new Vehicle { PlateNumber = "34 AKN 101", Brand = "Ford", Model = "Transit", VehicleType = VehicleType.Panelvan };
        var smallVan = new Vehicle { PlateNumber = "34 AKN 202", Brand = "Fiat", Model = "Doblo", VehicleType = VehicleType.SmallPanelvan };
        db.Vehicles.AddRange(van, smallVan);
        var vehicleOwner = driver ?? admin;
        db.DriverVehicles.AddRange(
            new DriverVehicle { DriverId = vehicleOwner.Id, VehicleId = van.Id, AssignedAt = now, AssignedByUserId = admin.Id },
            new DriverVehicle { DriverId = vehicleOwner.Id, VehicleId = smallVan.Id, AssignedAt = now, AssignedByUserId = admin.Id });

        // Yükleme noktaları '|' ile ayrılır; açıklama alanı yalnızca elle yazılan ek bilgidir.
        var rows = new (DateOnly Date, Customer Customer, VehicleType Vehicle, string Pickups, string Delivery, string Description, decimal Price)[]
        {
            (new(2026, 10, 19), starbucks, VehicleType.Panelvan, "Portashelf", "Haliç Tersane", "", 4000m),
            (new(2026, 10, 19), starbucks, VehicleType.Panelvan, "Gebze TLS", "Haliç Tersane", "", 8000m),
            (new(2026, 10, 20), starbucks, VehicleType.Panelvan, "Haliç Tersane", "Gebze TLS", "+1 eleman yevmiyesi - gece yükleme ertesi gün boşaltma", 10000m),
            (new(2026, 10, 20), starbucks, VehicleType.Panelvan, "Haliç Tersane", "Dudullu Hokuha Halı", "", 4000m),
            (new(2026, 10, 20), calvin, VehicleType.SmallPanelvan, "Ümraniye|İkitelli YS Metal", "Zorlu AVM", "", 4000m),
            (new(2026, 10, 21), calvin, VehicleType.SmallPanelvan, "İkitelli Buroser", "Zorlu AVM", "", 3500m),
            (new(2026, 10, 22), bestseller, VehicleType.Panelvan, "Akpınar Depo", "Beylikdüzü 2000 Reklam", "Vadi İstanbul Bestseller iadeleri", 3000m),
        };

        var assignee = driver ?? admin;
        foreach (var r in rows)
        {
            var startedAt = r.Date.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc);
            var job = new TransportJob
            {
                JobNumber = await numberGenerator.NextJobNumberAsync(r.Date, ct),
                CustomerId = r.Customer.Id,
                CreatedByUserId = admin.Id,
                AssignedDriverId = assignee.Id,
                JobDate = r.Date,
                StoreName = r.Customer.CompanyName,
                VehicleType = r.Vehicle,
                Description = r.Description,
                Price = r.Price,
                CreatedAt = startedAt.AddHours(-3),
            };
            job.SetStops([
                .. r.Pickups.Split('|').Select(p => new JobStopInput(StopType.Pickup, p, null, null, null)),
                new JobStopInput(StopType.Delivery, r.Delivery, null, null, null),
            ], now);
            job.ForceStatusForSeed(JobStatus.Completed, startedAt, startedAt.AddHours(4));
            AddHistory(job, admin.Id, assignee.Id, startedAt);
            db.TransportJobs.Add(job);
        }

        // Harita ve şoför akışını denemek için açık işler.
        var today = DateOnly.FromDateTime(now);
        var open1 = new TransportJob
        {
            CustomerId = starbucks.Id, CreatedByUserId = admin.Id, JobDate = today, StoreName = "Starbucks",
            VehicleType = VehicleType.Panelvan, Price = 8000m,
        };
        open1.SetStops([
            new JobStopInput(StopType.Pickup, "Gebze TLS", 40.8026, 29.4307, null),
            new JobStopInput(StopType.Delivery, "Haliç Tersane", 41.0383, 28.9469, null),
        ], now);
        open1.JobNumber = await numberGenerator.NextJobNumberAsync(today, ct);
        open1.Initialize(admin.Id, assignee.Id, now, van.Id);
        var open2 = new TransportJob
        {
            CustomerId = calvin.Id, CreatedByUserId = admin.Id, JobDate = today.AddDays(1), StoreName = "Calvin Klein",
            VehicleType = VehicleType.SmallPanelvan, Price = 4000m,
        };
        open2.SetStops([
            new JobStopInput(StopType.Pickup, "Ümraniye Depo", null, null, null),
            new JobStopInput(StopType.Pickup, "İkitelli YS Metal", null, null, null),
            new JobStopInput(StopType.Delivery, "Zorlu AVM", null, null, null),
        ], now);
        open2.JobNumber = await numberGenerator.NextJobNumberAsync(today, ct);
        open2.Initialize(admin.Id, null, now);
        db.TransportJobs.AddRange(open1, open2);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Örnek veriler oluşturuldu: 3 müşteri, 9 iş (7 tamamlanmış, toplam 36.500 TL).");
    }

    private static void AddHistory(TransportJob job, Guid adminId, Guid driverId, DateTime startedAt)
    {
        var steps = new (JobStatus? From, JobStatus To, Guid Actor, int Minutes, string Note)[]
        {
            (null, JobStatus.Assigned, adminId, -180, "İş oluşturuldu ve şoföre atandı."),
            (JobStatus.Assigned, JobStatus.Accepted, driverId, -120, "İş kabul edildi."),
            (JobStatus.Accepted, JobStatus.OnTheWay, driverId, 0, "Şoför yola çıktı."),
            (JobStatus.OnTheWay, JobStatus.Loading, driverId, 60, "Yükleme başladı."),
            (JobStatus.Loading, JobStatus.Delivering, driverId, 120, "Teslimata çıkıldı."),
            (JobStatus.Delivering, JobStatus.Completed, driverId, 240, "İş tamamlandı."),
        };
        foreach (var s in steps)
        {
            job.StatusHistory.Add(new JobStatusHistory
            {
                JobId = job.Id, FromStatus = s.From, ToStatus = s.To, ChangedByUserId = s.Actor, ChangedAt = startedAt.AddMinutes(s.Minutes), Description = s.Note,
            });
        }
    }
}
