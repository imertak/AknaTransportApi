using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AknaTransportApi.Business.Tests.Integration;

public class TransportOfferTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string Base = "/api/transport-offers";

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Europe/Istanbul"));

    private static TransportOfferRequest NewOffer(Guid? customerId = null, string? company = null) => new()
    {
        CustomerId = customerId,
        Customer = new OfferCustomerRequest
        {
            CompanyName = company ?? $"Çağlayan Lojistik {Guid.NewGuid():N}"[..28],
            ContactPerson = "Ayşe Öztürk",
            PhoneNumber = "0532 111 22 33",
            Email = "ayse@caglayan.example",
            Address = "İkitelli OSB, Başakşehir / İstanbul",
        },
        LoadingPoint = "İstanbul / Tuzla",
        LoadingAddress = "Aydınlı Mah. Gül Sok. No:5",
        UnloadingPoint = "İzmir / Kemalpaşa",
        UnloadingAddress = "Kemalpaşa OSB 3. Cadde",
        TransportType = TransportType.Full,
        VehicleType = VehicleType.Truck,
        CargoDescription = "12 palet tekstil ürünü, istiflenebilir",
        LoadingDate = Today.AddDays(2),
        DeliveryDate = Today.AddDays(3),
        DistanceKm = 565.4m,
        Items =
        [
            new TransportOfferItemRequest { Description = "Nakliye bedeli", Quantity = 1, Unit = "Sefer", UnitPrice = 10_000m },
            new TransportOfferItemRequest { Description = "Bekleme bedeli", Quantity = 3, Unit = "Saat", UnitPrice = 500m },
        ],
        DiscountAmount = 500m,
        VatRate = 20,
        PaymentTerms = "Teslimattan sonra 30 gün içinde havale/EFT.",
        DeliveryTerms = "Yükleme ve boşaltma alıcı tarafından yapılır.",
        CustomerNotes = "Sigorta bedeli fiyata dahildir.",
        InternalNotes = "Müşteri fiyat konusunda hassas.",
    };

    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Create_calculates_totals_on_server_and_creates_customer_with_offer_number()
    {
        var admin = await _client.AdminTokenAsync();
        var request = NewOffer();

        // İstemcinin gönderdiği toplamlar yok sayılır.
        var body = new
        {
            request.Customer, request.LoadingPoint, request.LoadingAddress, request.UnloadingPoint, request.UnloadingAddress,
            request.TransportType, request.VehicleType, request.CargoDescription, request.LoadingDate, request.DeliveryDate, request.DistanceKm,
            request.Items, request.DiscountAmount, request.VatRate, request.PaymentTerms, request.DeliveryTerms, request.CustomerNotes, request.InternalNotes,
            subTotal = 1m, vatAmount = 1m, totalAmount = 1m,
        };
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, body, admin);

        Assert.Matches(@"^AKT-\d{4}-\d{6}$", offer.OfferNumber);
        Assert.Equal(TransportOfferStatus.Draft, offer.Status);
        Assert.Equal(11_500m, offer.SubTotal);   // 10.000 + 3 x 500
        Assert.Equal(500m, offer.DiscountAmount);
        Assert.Equal(2_200m, offer.VatAmount);   // (11.500 - 500) x %20
        Assert.Equal(13_200m, offer.TotalAmount);
        Assert.Equal("TRY", offer.Currency);
        Assert.Equal(Today, offer.OfferDate);
        Assert.Equal(Today.AddDays(7), offer.ValidUntil); // varsayılan 7 gün
        Assert.Equal(2, offer.Items.Count);
        Assert.Equal(1_500m, offer.Items[1].TotalPrice);
        Assert.Equal("Saat", offer.Items[1].Unit);
        Assert.Equal($"AKNA_Teklif_{offer.OfferNumber}.pdf", offer.PdfFileName);
        Assert.Single(offer.History);
        Assert.True(offer.CanEdit && offer.CanSend && offer.CanAccept && !offer.CanConvert);

        // Yeni müşteri mevcut müşteri altyapısıyla oluşturulur.
        var customer = await _client.OkAsync<CustomerDto>(HttpMethod.Get, $"/api/customers/{offer.CustomerId}", token: admin);
        Assert.Equal(request.Customer.CompanyName, customer.CompanyName);
        Assert.Equal("Ayşe Öztürk", customer.ContactPerson);
        Assert.Equal("ayse@caglayan.example", customer.Email);

        // Oluşturan kullanıcı JWT'den alınır.
        var me = await _client.OkAsync<UserDto>(HttpMethod.Get, "/api/auth/me", token: admin);
        Assert.Equal(me.Id, offer.CreatedByUserId);
    }

    [Fact]
    public async Task Vat_and_line_totals_are_rounded_half_away_from_zero()
    {
        var admin = await _client.AdminTokenAsync();
        var request = NewOffer();
        request.Items = [new TransportOfferItemRequest { Description = "Nakliye", Quantity = 3, UnitPrice = 333.33m }];
        request.DiscountAmount = 0;
        request.VatRate = 20;
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, request, admin);
        Assert.Equal(999.99m, offer.SubTotal);
        Assert.Equal(200.00m, offer.VatAmount); // 199,998 → 200,00
        Assert.Equal(1_199.99m, offer.TotalAmount);

        request.VatRate = 0;
        request.Items = [new TransportOfferItemRequest { Description = "Nakliye", Quantity = 1.5m, UnitPrice = 0.05m }];
        var zero = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, request, admin);
        Assert.Equal(0.08m, zero.SubTotal); // 0,075 → 0,08
        Assert.Equal(0m, zero.VatAmount);
        Assert.Equal(0.08m, zero.TotalAmount);
    }

    [Fact]
    public async Task Invalid_requests_are_rejected()
    {
        var admin = await _client.AdminTokenAsync();

        var tooMuchDiscount = NewOffer();
        tooMuchDiscount.DiscountAmount = 11_500m;
        var (s1, b1) = await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, tooMuchDiscount, admin);
        Assert.Equal(HttpStatusCode.BadRequest, s1);
        Assert.Contains(b1!.Errors, e => e.Field == "discountAmount");

        var noItems = NewOffer();
        noItems.Items = [];
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, noItems, admin)).Status);

        var badVehicle = NewOffer();
        badVehicle.VehicleType = "Uzay gemisi";
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, badVehicle, admin)).Status);

        var usd = NewOffer();
        usd.Currency = "USD";
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, usd, admin)).Status);

        var badDates = NewOffer();
        badDates.OfferDate = Today;
        badDates.ValidUntil = Today.AddDays(-1);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, badDates, admin)).Status);

        var unknownCustomer = NewOffer(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, unknownCustomer, admin)).Status);
    }

    [Fact]
    public async Task Drivers_and_anonymous_users_cannot_access_offers()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(), admin);

        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, Base, null),
            (HttpMethod.Get, $"{Base}/stats", null),
            (HttpMethod.Get, $"{Base}/{offer.Id}", null),
            (HttpMethod.Get, $"{Base}/{offer.Id}/pdf", null),
            (HttpMethod.Post, Base, NewOffer()),
            (HttpMethod.Put, $"{Base}/{offer.Id}", NewOffer()),
            (HttpMethod.Post, $"{Base}/{offer.Id}/copy", null),
            (HttpMethod.Post, $"{Base}/{offer.Id}/send", null),
            (HttpMethod.Post, $"{Base}/{offer.Id}/accept", new OfferStatusRequest()),
            (HttpMethod.Post, $"{Base}/{offer.Id}/reject", new OfferStatusRequest()),
            (HttpMethod.Post, $"{Base}/{offer.Id}/cancel", new OfferStatusRequest { Reason = "x" }),
            (HttpMethod.Post, $"{Base}/{offer.Id}/convert-to-job", null),
            (HttpMethod.Delete, $"{Base}/{offer.Id}", null),
        };
        foreach (var (method, url, body) in calls)
        {
            var (driverStatus, _) = await _client.SendAsync<object>(method, url, body, driver.Token);
            Assert.True(driverStatus == HttpStatusCode.Forbidden, $"{method} {url} şoför için {(int)driverStatus}");
            var (anonStatus, _) = await _client.SendAsync<object>(method, url, body);
            Assert.True(anonStatus == HttpStatusCode.Unauthorized, $"{method} {url} oturumsuz {(int)anonStatus}");
        }

        // Şoför isteği hiçbir şeyi değiştirmedi.
        var after = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Get, $"{Base}/{offer.Id}", token: admin);
        Assert.Equal(TransportOfferStatus.Draft, after.Status);
        Assert.Single(after.History);

        // Var olmayan kayıt (tahmin edilen id) 404.
        var (missing, _) = await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Get, $"{Base}/{Guid.NewGuid()}", token: admin);
        Assert.Equal(HttpStatusCode.NotFound, missing);
    }

    [Fact]
    public async Task Status_flow_records_history_and_locks_accepted_offer()
    {
        var admin = await _client.AdminTokenAsync();
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(), admin);

        var sent = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/send", null, admin);
        Assert.Equal(TransportOfferStatus.Pending, sent.Status);
        Assert.NotNull(sent.SentAt);
        // Tekrar gönderme hata vermez, ikinci geçmiş kaydı oluşmaz.
        var again = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/send", null, admin);
        Assert.Equal(2, again.History.Count);

        // Yanıt bekleyen teklif düzenlenebilir; tutarlar yeniden hesaplanır.
        var edit = NewOffer(offer.CustomerId, offer.CustomerName);
        edit.Items = [new TransportOfferItemRequest { Description = "Nakliye bedeli", Quantity = 1, UnitPrice = 12_000m }];
        edit.DiscountAmount = 0;
        var updated = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Put, $"{Base}/{offer.Id}", edit, admin);
        Assert.Equal(14_400m, updated.TotalAmount);
        Assert.Equal(TransportOfferStatus.Pending, updated.Status);
        Assert.Equal(offer.OfferNumber, updated.OfferNumber);

        var accepted = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/accept", new OfferStatusRequest { Reason = "Telefonla onaylandı" }, admin);
        Assert.Equal(TransportOfferStatus.Accepted, accepted.Status);
        Assert.True(accepted.CanConvert);
        Assert.False(accepted.CanEdit);

        // Kabul edilen teklifin fiyatı değiştirilemez, durumu geri alınamaz, silinemez.
        var (editStatus, editBody) = await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Put, $"{Base}/{offer.Id}", edit, admin);
        Assert.Equal(HttpStatusCode.Conflict, editStatus);
        Assert.Contains("kopyalayın", editBody!.Message);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/reject", new OfferStatusRequest(), admin)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/send", null, admin)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync<object>(HttpMethod.Delete, $"{Base}/{offer.Id}", null, admin)).Status);

        var detail = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Get, $"{Base}/{offer.Id}", token: admin);
        Assert.Equal(
            [TransportOfferStatus.Draft, TransportOfferStatus.Pending, TransportOfferStatus.Pending, TransportOfferStatus.Accepted],
            detail.History.Select(h => h.NewStatus).ToArray());
        Assert.Equal(TransportOfferStatus.Pending, detail.History[^1].OldStatus);
        Assert.Contains("Telefonla onaylandı", detail.History[^1].Description);
        Assert.All(detail.History, h => Assert.NotNull(h.ChangedByUserName));
    }

    [Fact]
    public async Task Rejected_and_cancelled_offers_are_final()
    {
        var admin = await _client.AdminTokenAsync();
        var a = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(), admin);
        var rejected = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{a.Id}/reject", new OfferStatusRequest { Reason = "Fiyat yüksek" }, admin);
        Assert.Equal(TransportOfferStatus.Rejected, rejected.Status);
        Assert.Equal("Fiyat yüksek", rejected.RejectionReason);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{a.Id}/accept", new OfferStatusRequest(), admin)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Put, $"{Base}/{a.Id}", NewOffer(a.CustomerId), admin)).Status);

        var b = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(), admin);
        var (noReason, _) = await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{b.Id}/cancel", new OfferStatusRequest(), admin);
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        var cancelled = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{b.Id}/cancel", new OfferStatusRequest { Reason = "Müşteri vazgeçti" }, admin);
        Assert.Equal(TransportOfferStatus.Cancelled, cancelled.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{b.Id}/accept", new OfferStatusRequest(), admin)).Status);
    }

    [Fact]
    public async Task Accepted_offer_converts_to_job_once_with_all_details()
    {
        var admin = await _client.AdminTokenAsync();
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(), admin);

        // Kabul edilmemiş teklif dönüştürülemez.
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync<ConvertOfferResultDto>(HttpMethod.Post, $"{Base}/{offer.Id}/convert-to-job", null, admin)).Status);

        await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/accept", new OfferStatusRequest(), admin);
        var result = await _client.OkAsync<ConvertOfferResultDto>(HttpMethod.Post, $"{Base}/{offer.Id}/convert-to-job", null, admin);
        Assert.Equal(result.JobId, result.Offer.ConvertedJobId);
        Assert.Equal(result.JobNumber, result.Offer.ConvertedJobNumber);
        Assert.False(result.Offer.CanConvert);
        Assert.False(result.Offer.CanCancel);
        Assert.Contains(result.JobNumber, result.Offer.History[^1].Description);

        var job = (await _client.OkAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{result.JobId}", token: admin)).Job;
        Assert.Equal(offer.CustomerId, job.CustomerId);
        Assert.Equal(11_000m, job.Price); // indirim sonrası KDV hariç tutar
        Assert.Equal(VehicleType.Truck, job.VehicleType);
        Assert.Equal(offer.LoadingDate, job.JobDate);
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal("İstanbul / Tuzla", job.PickupAddress);
        Assert.Equal("İzmir / Kemalpaşa", job.DeliveryAddress);
        Assert.Equal("Aydınlı Mah. Gül Sok. No:5", job.Stops.Single(s => s.StopType == StopType.Pickup).Description);
        Assert.Contains("12 palet tekstil", job.Description);
        Assert.Contains(offer.OfferNumber, job.Notes);
        Assert.Contains(job.Contacts, c => c.Name == "Ayşe Öztürk" && c.PhoneNumber == "0532 111 22 33");

        // İkinci dönüştürme reddedilir; iş sayısı değişmez.
        var (second, secondBody) = await _client.SendAsync<ConvertOfferResultDto>(HttpMethod.Post, $"{Base}/{offer.Id}/convert-to-job", null, admin);
        Assert.Equal(HttpStatusCode.Conflict, second);
        Assert.Contains("zaten", secondBody!.Message);
        Assert.Equal(1, await QueryAsync(db => db.TransportJobs.CountAsync(j => j.Notes!.Contains(offer.OfferNumber))));

        // İşe dönüştürülmüş teklif iptal edilemez, silinemez.
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/cancel", new OfferStatusRequest { Reason = "x" }, admin)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync<object>(HttpMethod.Delete, $"{Base}/{offer.Id}", null, admin)).Status);
    }

    [Fact]
    public async Task Concurrent_conversions_create_a_single_job()
    {
        var admin = await _client.AdminTokenAsync();
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(), admin);
        await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/accept", new OfferStatusRequest(), admin);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            _client.SendAsync<ConvertOfferResultDto>(HttpMethod.Post, $"{Base}/{offer.Id}/convert-to-job", null, admin)));
        Assert.Single(results, r => r.Status == HttpStatusCode.OK);
        Assert.Equal(1, await QueryAsync(db => db.TransportJobs.CountAsync(j => j.Notes!.Contains(offer.OfferNumber))));
    }

    [Fact]
    public async Task Conversion_rolls_back_when_job_creation_fails()
    {
        var admin = await _client.AdminTokenAsync();
        var type = await _client.OkAsync<VehicleTypeDto>(HttpMethod.Post, "/api/vehicle-types", new VehicleTypeRequest { Name = $"Frigo {Guid.NewGuid():N}"[..14] }, admin);
        var request = NewOffer();
        request.VehicleType = type.Code;
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, request, admin);
        Assert.Equal(type.Name, offer.VehicleTypeName);
        await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/accept", new OfferStatusRequest(), admin);

        // Araç tipi pasife alınırsa iş oluşturma servisi reddeder; teklif dönüştürülmüş sayılmaz.
        await _client.OkAsync<VehicleTypeDto>(HttpMethod.Put, $"/api/vehicle-types/{type.Id}", new VehicleTypeRequest { Name = type.Name, IsActive = false }, admin);
        var (status, _) = await _client.SendAsync<ConvertOfferResultDto>(HttpMethod.Post, $"{Base}/{offer.Id}/convert-to-job", null, admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        var after = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Get, $"{Base}/{offer.Id}", token: admin);
        Assert.Null(after.ConvertedJobId);
        Assert.True(after.CanConvert);
        Assert.Equal(0, await QueryAsync(db => db.TransportJobs.IgnoreQueryFilters().CountAsync(j => j.Notes!.Contains(offer.OfferNumber))));

        // Tip yeniden açılınca dönüştürme çalışır.
        await _client.OkAsync<VehicleTypeDto>(HttpMethod.Put, $"/api/vehicle-types/{type.Id}", new VehicleTypeRequest { Name = type.Name, IsActive = true }, admin);
        await _client.OkAsync<ConvertOfferResultDto>(HttpMethod.Post, $"{Base}/{offer.Id}/convert-to-job", null, admin);
    }

    [Fact]
    public async Task Expired_offer_is_derived_and_must_be_extended_before_accepting()
    {
        var admin = await _client.AdminTokenAsync();
        var request = NewOffer();
        request.OfferDate = Today.AddDays(-20);
        request.ValidUntil = Today.AddDays(-13);
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, request, admin);
        Assert.Equal(TransportOfferStatus.Expired, offer.Status);
        Assert.Equal(TransportOfferStatus.Draft, offer.StoredStatus);
        Assert.False(offer.CanAccept);
        Assert.False(offer.CanSend);
        Assert.True(offer.CanEdit);

        var (acceptStatus, body) = await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/accept", new OfferStatusRequest(), admin);
        Assert.Equal(HttpStatusCode.Conflict, acceptStatus);
        Assert.Contains("geçerlilik", body!.Message);

        var list = await _client.OkAsync<PagedResult<TransportOfferListItemDto>>(HttpMethod.Get, $"{Base}?status=Expired&search={offer.OfferNumber}", token: admin);
        Assert.Equal(TransportOfferStatus.Expired, Assert.Single(list.Items).Status);
        var drafts = await _client.OkAsync<PagedResult<TransportOfferListItemDto>>(HttpMethod.Get, $"{Base}?status=Draft&search={offer.OfferNumber}", token: admin);
        Assert.Empty(drafts.Items);

        // Geçerlilik uzatılınca teklif yeniden taslak olur ve kabul edilebilir.
        request.CustomerId = offer.CustomerId;
        request.ValidUntil = Today.AddDays(5);
        var extended = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Put, $"{Base}/{offer.Id}", request, admin);
        Assert.Equal(TransportOfferStatus.Draft, extended.Status);
        await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/accept", new OfferStatusRequest(), admin);
    }

    [Fact]
    public async Task Copy_creates_new_draft_with_new_number()
    {
        var admin = await _client.AdminTokenAsync();
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(), admin);
        await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/reject", new OfferStatusRequest(), admin);

        var copy = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{offer.Id}/copy", null, admin);
        Assert.NotEqual(offer.OfferNumber, copy.OfferNumber);
        Assert.NotEqual(offer.Id, copy.Id);
        Assert.Equal(TransportOfferStatus.Draft, copy.Status);
        Assert.Equal(offer.Id, copy.CopiedFromOfferId);
        Assert.Equal(offer.TotalAmount, copy.TotalAmount);
        Assert.Equal(offer.Items.Select(i => (i.Description, i.Quantity, i.UnitPrice)), copy.Items.Select(i => (i.Description, i.Quantity, i.UnitPrice)));
        Assert.Equal(Today.AddDays(7), copy.ValidUntil);
        Assert.Contains(offer.OfferNumber, Assert.Single(copy.History).Description);
    }

    [Fact]
    public async Task Soft_deleted_offer_disappears_but_row_is_kept()
    {
        var admin = await _client.AdminTokenAsync();
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(), admin);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync<object>(HttpMethod.Delete, $"{Base}/{offer.Id}", null, admin)).Status);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync<object>(HttpMethod.Delete, $"{Base}/{offer.Id}", null, admin)).Status); // idempotent
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync<TransportOfferDetailDto>(HttpMethod.Get, $"{Base}/{offer.Id}", token: admin)).Status);
        var list = await _client.OkAsync<PagedResult<TransportOfferListItemDto>>(HttpMethod.Get, $"{Base}?search={offer.OfferNumber}", token: admin);
        Assert.Empty(list.Items);
        Assert.True(await QueryAsync(db => db.TransportOffers.IgnoreQueryFilters().AnyAsync(o => o.Id == offer.Id && o.IsDeleted && o.DeletedByUserId != null)));
    }

    [Fact]
    public async Task List_search_filters_and_stats()
    {
        var admin = await _client.AdminTokenAsync();
        var company = $"Filtre Gıda {Guid.NewGuid():N}"[..24];
        var first = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(company: company), admin);
        var second = NewOffer(first.CustomerId, company);
        second.OfferDate = new DateOnly(2026, 1, 10);
        second.ValidUntil = new DateOnly(2026, 1, 17);
        await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, second, admin);
        var third = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(first.CustomerId, company), admin);
        await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, $"{Base}/{third.Id}/accept", new OfferStatusRequest(), admin);

        var byCustomer = await _client.OkAsync<PagedResult<TransportOfferListItemDto>>(HttpMethod.Get, $"{Base}?search={Uri.EscapeDataString(company.ToLowerInvariant())}", token: admin);
        Assert.Equal(3, byCustomer.TotalCount);
        Assert.All(byCustomer.Items, i => Assert.Equal(company, i.CustomerName));

        var byNumber = await _client.OkAsync<PagedResult<TransportOfferListItemDto>>(HttpMethod.Get, $"{Base}?search={first.OfferNumber}", token: admin);
        Assert.Equal(first.Id, Assert.Single(byNumber.Items).Id);

        var byDate = await _client.OkAsync<PagedResult<TransportOfferListItemDto>>(HttpMethod.Get,
            $"{Base}?customerId={first.CustomerId}&startDate=2026-01-01&endDate=2026-01-31", token: admin);
        Assert.Equal(TransportOfferStatus.Expired, Assert.Single(byDate.Items).Status);

        var accepted = await _client.OkAsync<PagedResult<TransportOfferListItemDto>>(HttpMethod.Get, $"{Base}?customerId={first.CustomerId}&status=Accepted", token: admin);
        Assert.Equal(third.Id, Assert.Single(accepted.Items).Id);

        var stats = await _client.OkAsync<TransportOfferStatsDto>(HttpMethod.Get, $"{Base}/stats?customerId={first.CustomerId}", token: admin);
        Assert.Equal(3, stats.Total);
        Assert.Equal(1, stats.Draft);
        Assert.Equal(1, stats.Accepted);
        Assert.Equal(1, stats.Expired);
        Assert.Equal(0, stats.Pending);
        Assert.Equal(third.TotalAmount, stats.AcceptedAmount);
    }

    [Fact]
    public async Task Pdf_is_generated_with_offer_file_name()
    {
        var admin = await _client.AdminTokenAsync();
        var offer = await _client.OkAsync<TransportOfferDetailDto>(HttpMethod.Post, Base, NewOffer(), admin);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/{offer.Id}/pdf");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"AKNA_Teklif_{offer.OfferNumber}.pdf", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 5_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
