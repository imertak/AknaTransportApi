# AKNA Transport API

AKNA Transport nakliye ve lojistik yönetim sisteminin backend'i. .NET 9, ASP.NET Core Web API, EF Core ve PostgreSQL ile yazılmıştır. Mobil uygulama: [AknaTransportNative](https://github.com/imertak/AknaTransportNative).

## İçindekiler

1. [Mimari](#mimari)
2. [Hızlı başlangıç](#hızlı-başlangıç)
3. [Veritabanı ve migration](#veritabanı-ve-migration)
4. [Yapılandırma ve environment variable'lar](#yapılandırma-ve-environment-variablelar)
5. [Roller ve yetkiler](#roller-ve-yetkiler)
6. [Endpointler](#endpointler)
7. [İş akışı ve durumlar](#iş-akışı-ve-durumlar)
8. [Konum takibi](#konum-takibi)
9. [Fatura, e-Arşiv ve e-posta](#fatura-e-arşiv-ve-e-posta)
10. [SMS ile kullanıcı şifresi](#sms-ile-kullanıcı-şifresi)
11. [Bildirimler](#bildirimler)
12. [PDF iş dökümü](#pdf-iş-dökümü)
13. [Nakliye teklifleri](#nakliye-teklifleri)
14. [Mobil uygulama entegrasyonu](#mobil-uygulama-entegrasyonu)
15. [Testler](#testler)
16. [Gerçek sağlayıcı bilgisi bekleyen entegrasyonlar](#gerçek-sağlayıcı-bilgisi-bekleyen-entegrasyonlar)

## Mimari

Katman düzeni [MerfitCustomerApi](https://github.com/akna-solutions/MerfitCustomerApi) referans alınarak kuruldu (yalnızca klasör/proje organizasyonu; iş mantığı AKNA'ya özeldir).

```
AknaTransportApi.sln
├── AknaTransportApi.Domain           Entities, Enums, Common (Entity, Money), Exceptions,
│                                     Interfaces (IUnitOfWork, INumberGenerator), Interfaces/Repositories (IRepository)
├── AknaTransportApi.Business         Common (yanıt zarfı, sayfalama, hatalar, seçenekler), Dtos/<Özellik>,
│                                     Interfaces/Services (servis + entegrasyon sözleşmeleri), Services/<Özellik>,
│                                     Validators/<Özellik> (FluentValidation), Mappings
├── AknaTransportApi.Infrastructure   Persistence (AppDbContext, UnitOfWork, Repositories, Migrations, Seed),
│                                     Configurations (EF Fluent API), Pdf, Email, Sms, EArchive, Storage, Security, BackgroundJobs
├── AknaTransportApi.Api              Controllers, Middleware, Filters, Extensions (DI, JWT, Swagger, rate limit), Security, Program.cs
└── AknaTransportApi.Business.Tests   Unit + integration testleri
```

Her projenin `GlobalUsings.cs` dosyası yalnızca kendisinin ve bağımlı olduğu katmanların namespace'lerini içerir.

Bağımlılık yönü: `Api → Business, Infrastructure`, `Infrastructure → Business, Domain`, `Business → Domain`. Domain hiçbir projeye bağımlı değildir. Döngüsel bağımlılık yoktur.

- Controller'larda iş kuralı yoktur; tüm kurallar Business servislerinde ve Domain entity metotlarındadır (`TransportJob.Start()`, `Invoice.MarkIssued()` vb.).
- Entity'ler API'den dışarı açılmaz, her uç nokta Request/Response DTO kullanır.
- Paket sürümleri `Directory.Packages.props` içinde merkezi olarak yönetilir.

Kullanılan kütüphaneler: EF Core 9 + Npgsql, JWT Bearer, FluentValidation, Swashbuckle, Serilog, QuestPDF (Community lisans), MailKit.

> **QuestPDF lisansı:** Community lisansı yıllık cirosu 1 milyon USD'nin altındaki şirketler için ücretsizdir. Ciro bu sınırı aşarsa ticari lisans gerekir.

## Hızlı başlangıç

Gereksinimler: .NET 9 SDK, PostgreSQL 14+ (veya Docker).

```bash
# 1) PostgreSQL (yerel geliştirme)
docker compose up -d

# 2) Gizli ayarlar (Development için User Secrets; repoya yazılmaz)
cd AknaTransportApi.Api
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"
dotnet user-secrets set "Seed:AdminEmail" "admin@akna.local"
dotnet user-secrets set "Seed:AdminPassword" "GüçlüBirŞifre123"
dotnet user-secrets set "Seed:DemoUserPassword" "Sofor12345"   # örnek şoför hesapları için (isteğe bağlı)

# 3) Çalıştır (Development'ta migration'lar otomatik uygulanır ve örnek veri oluşturulur)
dotnet run
```

Swagger: <http://localhost:5080/swagger>. Sağlık kontrolü: `GET /health`.

Development örnek verisi (isteğe bağlı): Starbucks, Calvin Klein ve Bestseller müşterileri ile 19–22 Ekim 2026 tarihli 7 tamamlanmış iş (toplam 36.500 TL), ayrıca harita denemesi için 2 açık iş. `Seed:DemoUserPassword` verilirse `ahmet.sofor@akna.local` ve `mehmet.sofor@akna.local` şoför hesapları da oluşturulur. Örnek veri varsayılan olarak **kapalıdır**; yalnızca Development ortamında `Seed:DemoData=true` verilirse ve müşteri tablosu boşken oluşur. Mevcut kayıtlara dokunmaz.

İlk yönetici hesabı her ortamda, sistemde aktif yönetici yoksa `Seed:AdminEmail` ve `Seed:AdminPassword` değerleriyle oluşturulur. Şifre kaynak kodda yoktur.

## Veritabanı ve migration

Migration'lar `AknaTransportApi.Infrastructure/Persistence/Migrations` altındadır. EF aracı repo içinde sabitlenmiştir (`dotnet-tools.json`).

```bash
dotnet tool restore

# Yeni migration
dotnet ef migrations add <Ad> \
  --project AknaTransportApi.Infrastructure \
  --startup-project AknaTransportApi.Infrastructure \
  --output-dir Persistence/Migrations

# Veritabanını güncelle (bağlantı dizesi environment variable'dan okunur)
export AKNA_DESIGN_CONNECTION="Host=localhost;Port=5432;Database=akna_transport;Username=akna;Password=akna_dev_password"
dotnet ef database update --project AknaTransportApi.Infrastructure --startup-project AknaTransportApi.Infrastructure

# Üretim için idempotent SQL betiği
dotnet ef migrations script --idempotent -o migrate.sql \
  --project AknaTransportApi.Infrastructure --startup-project AknaTransportApi.Infrastructure
```

Hazır şema betiği `database/akna_transport_schema.sql` dosyasındadır. Boş bir PostgreSQL veritabanına doğrudan uygulanabilir ve tekrar çalıştırmak güvenlidir:

```bash
psql -h localhost -U akna -d akna_transport -f database/akna_transport_schema.sql
```

Betik yalnızca tabloları, index'leri ve sequence'leri oluşturur. İlk yönetici (`Seed:AdminEmail` / `Seed:AdminPassword`) ve Development'taki örnek veriler API ilk açıldığında eklenir. Yeni bir migration eklendiğinde betik yukarıdaki `migrations script` komutuyla yeniden üretilmelidir.

Alternatif olarak `Database__ApplyMigrationsOnStartup=true` ile uygulama açılışta bekleyen migration'ları uygular (Development'ta varsayılan açık, üretimde kapalı).

Veritabanı notları:
- Tüm tarih-saat alanları UTC (`timestamptz`) saklanır. İş ve fatura tarihleri `date` tipindedir.
- Para alanları `numeric(18,2)`; float/double kullanılmaz. KDV hesabı `MidpointRounding.AwayFromZero` ile 2 haneye yuvarlanır.
- Enum'lar okunabilir string olarak saklanır (`OnTheWay`, `Issued` ...).
- İş ve fatura numaraları PostgreSQL sequence'leriyle üretilir: `AKN-2026-000001`, `AKF-2026-000001`. Ayrıca unique index ile korunur.
- `TransportJob` ve `Invoice` üzerinde iyimser eşzamanlılık kontrolü (`ConcurrencyStamp`) vardır; çakışmada 409 döner.

## Yapılandırma ve environment variable'lar

`appsettings.json` içinde hassas bilgi yoktur. Üretimde aşağıdaki değerleri environment variable olarak verin (örnek: [`deploy.env.example`](deploy.env.example)). İç içe anahtarlar `__` ile yazılır.

| Değişken | Zorunlu | Açıklama |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | Evet | PostgreSQL bağlantı dizesi |
| `Jwt__Secret` | Evet | En az 32 karakter. Kısa ise uygulama açılmaz |
| `Jwt__Issuer`, `Jwt__Audience` | Hayır | Varsayılan `AknaTransport`, `AknaTransport.Mobile` |
| `Jwt__AccessTokenExpirationMinutes` | Hayır | Varsayılan 30 |
| `Jwt__RefreshTokenExpirationDays` | Hayır | Varsayılan 30 |
| `Seed__AdminEmail`, `Seed__AdminPassword` | İlk kurulumda | Aktif yönetici yoksa oluşturulur |
| `Email__Provider` | Fatura e-postası için | `Resend` veya `Smtp` (varsayılan) |
| `Email__ResendApiKey` | Resend için | Resend panelinden alınan API anahtarı (`re_...`). `Email__FromEmail` adresinin alan adı Resend'de doğrulanmış olmalıdır. |
| `Email__Host`, `Email__Port`, `Email__Username`, `Email__Password`, `Email__FromEmail`, `Email__FromName` | Fatura e-postası için | `Email__FromEmail` / `Email__FromName` her iki sağlayıcıda kullanılır; diğerleri yalnızca SMTP. `Email__Security`: Auto / StartTls / SslOnConnect / None. Gmail: `smtp.gmail.com`, 587, `StartTls`, kullanıcı ve gönderen Gmail adresi, şifre Google hesabındaki **uygulama şifresi** (2 adımlı doğrulama açık olmalı). |
| `Pdf__ChromePath` | Hayır | GİB e-Arşiv portalındaki imzalı fatura (HTML) bu tarayıcıyla PDF'e çevrilip e-postaya eklenir. Boşsa bilinen kurulum yerleri denenir; Windows'ta Edge otomatik bulunur. Linux sunucuda `sudo apt install chromium` yeterlidir. Tarayıcı yoksa GİB no ve ETTN ile üretilen PDF gönderilir, GİB belgesi HTML olarak eklenir. |
| `Email__AlwaysCc` | Hayır | Her gönderilen e-postaya CC olarak eklenen adres(ler), `;` ile ayrılır. Varsayılan `imertakpinar0@gmail.com`. |
| `Sms__Provider`, `Sms__ApiUrl`, `Sms__Username`, `Sms__Password`, `Sms__ApiKey`, `Sms__Sender` | SMS için | Bkz. [SMS](#sms-ile-kullanıcı-şifresi) |
| `Push__Enabled` | Hayır | Telefon bildirimleri (Expo Push). Varsayılan `true`; `false` ise yalnızca uygulama içi bildirim listesi çalışır. |
| `Push__ExpoAccessToken` | Hayır | Expo hesabında "Enhanced push security" açıksa Expo panelinden alınan erişim anahtarı. |
| `EArchive__Provider` | Gerçek fatura için | `GibPortal` (GİB e-Arşiv portalı, yerleşik) veya eklenen özel entegratörün adı. Boşsa e-Arşiv kapalıdır. |
| `EArchive__Username`, `EArchive__Password` | GİB portalı için | e-Arşiv portalı kullanıcı kodu ve şifresi (İnteraktif Vergi Dairesi'nden alınır). |
| `EArchive__UseTestEnvironment` | Hayır | `true`: GİB test portalı (earsivportaltest.efatura.gov.tr) |
| `EArchive__ApiUrl`, `EArchive__ApiKey` | Özel entegratör için | Bkz. [e-Arşiv](#fatura-e-arşiv-ve-e-posta) |
| `App__CompanyIban`, `App__CompanyBankName` | Önerilir | Faturanın açıklamasına yazılan IBAN ve banka adı (istekte IBAN verilmezse). Teklif PDF'inin koşullar bölümünde de gösterilir. |
| `App__CompanyAddress`, `App__CompanyPhone`, `App__CompanyEmail`, `App__CompanyWebsite` | Önerilir | Teklif PDF'inin başlığında logonun yanında yazılan firma iletişim bilgileri. Boş olan satır PDF'te gösterilmez. |
| `App__CompanyTaxOffice`, `App__CompanyTaxNumber` | Hayır | Teklif PDF'inin başlığındaki vergi dairesi ve vergi numarası |
| `App__DefaultWithholdingCode` | Hayır | Varsayılan KDV tevkifat kodu (ör. `624`). Boşsa faturalar tevkifatsız açılır. |
| `App__PasswordResetUrl` | Hayır | Şifre sıfırlama bağlantısı, `{token}` ve `{email}` yer tutucuları |
| `App__TrackingUrl` | Hayır | Müşteri takip bağlantısı, `{token}` yer tutucusu (yoksa `?t=<token>` eklenir). Boşsa API'nin kendi takip sayfası kullanılır: `https://<api>/takip/?t=<token>` |
| `App__TimeZoneId` | Hayır | Varsayılan `Europe/Istanbul` |
| `Cors__AllowedOrigins__0..n` | Hayır | Web istemcileri için izinli origin'ler |
| `Storage__RootPath` | Hayır | e-Arşiv PDF'lerinin saklandığı klasör (web kökü dışında) |
| `LocationTracking__*` | Hayır | Konum filtresi ve saklama süresi (aşağıya bakın) |
| `RateLimiting__*` | Hayır | Dakika başı limitler |
| `Swagger__Enabled` | Hayır | Üretimde varsayılan kapalı |

## Roller ve yetkiler

İki rol vardır: `Admin` ve `User` (şoför). Ayrı bir "admin-şoför" rolü yoktur; Admin şoför işlemlerini de yapabilir.

| İşlem | Admin | User (şoför) |
|---|---|---|
| Kullanıcı ekleme / düzenleme | ✓ | ✗ |
| Müşteri, fatura, rapor, dashboard | ✓ | ✗ |
| İş oluşturma, düzenleme, silme, atama, iptal | ✓ | ✗ |
| İşi kendine atama (`assignToMe`, `claim`, `assign`) | ✓ | ✗ |
| İş listesi ve detay | Tüm işler | Yalnızca kendisine atanan |
| Kabul et / yola çık / yükleme / teslimat / tamamla | Tüm işler | Yalnızca kendisine atanan |
| Konum gönderme | Yalnızca kendisine atanan iş | Yalnızca kendisine atanan iş |
| Konum görüntüleme | Tüm işler | Yalnızca kendi işleri |

Yetkiler backend'de hem controller politikalarıyla hem servis katmanında kontrol edilir. Şoför filtre parametreleriyle (`driverId` vb.) bu kısıtı aşamaz. Başka şoförün işine erişim 403 döner.

**Açık kayıt yoktur.** Kullanıcıları yalnızca yönetici ekler (`POST /api/auth/register` veya `POST /api/users`). Şifreyi yönetici belirlemez; sistem rastgele geçici şifre üretir ve kullanıcının telefonuna SMS ile gönderir. Geçici şifreyle giriş yapan kullanıcı, şifresini değiştirene kadar yalnızca `/api/auth/me`, `/api/auth/change-password` ve `/api/auth/logout` uç noktalarını kullanabilir (diğerleri 403).

Güvenlik özeti:
- Şifreler PBKDF2-HMAC-SHA256 (310.000 iterasyon, rastgele salt) ile saklanır.
- JWT access token içinde `sub`/`nameidentifier` (UserId) ve `role` claim'leri bulunur.
- Refresh token'lar veritabanında yalnızca SHA-256 özeti olarak tutulur, her yenilemede döndürülür (rotation). İptal edilmiş bir token tekrar kullanılırsa kullanıcının tüm oturumları kapatılır.
- Rate limiting: giriş/şifre işlemleri IP başına dakikada 10, konum kullanıcı başına dakikada 30, genel 300 istek.
- EF Core parametreli sorgular kullanılır (SQL injection'a karşı); ham SQL yalnızca sabit sequence çağrısında vardır.
- İstek gövdesi, sorgu dizesi, token ve şifreler loglanmaz.
- PDF indirmeleri yalnızca yöneticiye açıktır; dosya yolları kullanıcıdan alınmaz, depolama path traversal'a karşı korunur.

## Endpointler

Tüm yanıtlar standart zarf içindedir:

```json
{ "isSuccess": true, "message": "İş oluşturuldu.", "data": { }, "errors": [] }
```

Hata örneği (400):

```json
{ "isSuccess": false, "message": "Doğrulama hatası.", "data": null,
  "errors": [ { "field": "price", "message": "Fiyat sıfırdan büyük olmalıdır." } ] }
```

Sayfalı sonuçlar: `data = { items, pageNumber, pageSize, totalCount, totalPages }`. Sorgu parametreleri: `pageNumber`, `pageSize` (en fazla 200), `sortBy`, `sortDirection` (`asc`/`desc`).

HTTP kodları: 200/201 başarı, 400 doğrulama, 401 oturum yok/geçersiz (süresi dolmuşsa `X-Token-Expired: true` başlığı), 403 yetki yok, 404 bulunamadı, 409 durum çakışması (ör. tamamlanmış işi yeniden başlatma, mükerrer fatura), 429 rate limit, 503 entegrasyon yapılandırılmamış.

### Kimlik doğrulama — `/api/auth`
| Metot | Yol | Yetki | Açıklama |
|---|---|---|---|
| POST | `/login` | Herkes | Giriş; access + refresh token |
| POST | `/register` | Admin | Kullanıcı ekler; geçici şifre SMS ile gider |
| POST | `/refresh-token` | Herkes | Token yenileme (rotation) |
| POST | `/logout` | Oturum | Refresh token iptali; `allDevices: true` ile tüm oturumlar |
| POST | `/forgot-password` | Herkes | Şifre sıfırlama e-postası (kullanıcı varlığı sızdırılmaz) |
| POST | `/reset-password` | Herkes | E-postadaki kod ile yeni şifre |
| POST | `/change-password` | Oturum | Şifre değiştirme, yeni token çifti döner |
| GET | `/me` | Oturum | Oturumdaki kullanıcı |

### Kullanıcılar — `/api/users` (Admin)
`GET /` (filtre: `role`, `isActive`, `search`), `GET /{id}`, `POST /`, `PUT /{id}`, `POST /{id}/reset-password` (yeni geçici şifre + SMS).

### Müşteriler — `/api/customers` (Admin)
`GET /` (`search`, `isActive`), `GET /{id}`, `POST /`, `PUT /{id}`, `DELETE /{id}`, `GET /{id}/jobs`, `GET /{id}/invoices`, `GET /{id}/summary`, `GET /{id}/addresses` (`stopType=Pickup|Delivery`, `search`, `limit`).

Adres geçmişi ayrı bir tabloda tutulmaz; müşterinin iptal edilmemiş işlerinin duraklarından tekilleştirilerek (son kullanıma göre, koordinatlarıyla) üretilir.

Geçmiş iş veya faturası olan müşteri silinmez, pasife alınır (`isActive=false`); kayıtlar bozulmaz. Özet: toplam/tamamlanan iş sayısı, toplam nakliye tutarı (tamamlanan işler), toplam fatura tutarı (kesilmiş faturalar), son 10 iş, son 20 fatura.

### İşler — `/api/jobs`
| Metot | Yol | Yetki |
|---|---|---|
| GET | `/` | Oturum (şoför yalnızca kendi işleri) |
| GET | `/my-jobs` | Oturum |
| GET | `/{id}` | Admin veya atanmış şoför (durum geçmişiyle birlikte). Admin `?includeDeleted=true` ile silinmiş işi de görür |
| GET | `/deleted` | Admin (silinmiş işler, en son silinen önce; `search`) |
| GET | `/{id}/history` | Admin veya atanmış şoför |
| POST | `/` | Admin |
| PUT | `/{id}` | Admin |
| DELETE | `/{id}` | Admin. Mantıksal silme (soft delete), her aşamada; gövde `{ "reason": "..." }` devam eden/tamamlanmış işte zorunlu. Faturada kullanılmış iş silinemez |
| PATCH | `/{id}/restore` | Admin (silinmiş işi geri alır) |
| PATCH | `/{id}/assign` | Admin (`driverId`, `vehicleId`; `driverId: null` atamayı kaldırır) |
| PATCH | `/{id}/claim` | Admin (işi üstlenir → Accepted; isteğe bağlı kendi aracı `vehicleId`) |
| PATCH | `/{id}/accept`, `/start`, `/start-loading`, `/start-delivery`, `/complete` | Admin veya atanmış şoför |
| PATCH | `/{id}/cancel` | Admin. Gövde `{ "reason": "..." }` zorunlu. Tamamlanmış/faturalı iş iptal edilemez; zaten iptal edilmiş işte mevcut hali döner |
| POST / DELETE | `/{id}/tracking-links` | Admin (müşteri takip bağlantısı oluştur / iptal et) |

Liste filtreleri: `startDate`, `endDate`, `customerId`, `driverId`, `status` (çoklu: `?status=Pending&status=Assigned`), `vehicleType`, `storeName`, `search`, `uninvoiced`, `billingStatus` (`NotInvoiced`, `InDraft`, `Invoiced`). Sıralama: `sortBy=jobDate|price|createdAt|jobNumber|status|storeName`.

İş oluşturma örneği (yeni müşteriyle ve kendine atayarak):

```json
POST /api/jobs
{
  "newCustomer": { "companyName": "Starbucks", "email": "fatura@starbucks.example" },
  "jobDate": "2026-10-19",
  "storeName": "Starbucks",
  "vehicleType": "Panelvan",
  "stops": [
    { "stopType": "Pickup", "address": "İstanbul / Ümraniye", "latitude": 41.0165, "longitude": 29.1248 },
    { "stopType": "Pickup", "address": "İstanbul / İkitelli" },
    { "stopType": "Delivery", "address": "İstanbul / Zorlu AVM" },
    { "stopType": "Delivery", "address": "İstanbul / Haliç Tersane" }
  ],
  "description": "+1 eleman yevmiyesi",
  "price": 4000,
  "assignToMe": true,
  "assignedVehicleId": "…"
}
```

Çoklu durak kuralları:
- Duraklar `transport_job_stops` tablosunda tutulur; her tip için `Sequence` 1'den başlar ve `(TransportJobId, StopType, Sequence)` benzersizdir. En az bir yükleme ve bir teslimat noktası zorunludur.
- `routeDescription` otomatik üretilir: önce tüm yüklemeler, sonra tüm teslimatlar `" - "` ile birleştirilir. Duraklar değişince yeniden üretilir; kullanıcının yazdığı `description` ayrı tutulur ve kaybolmaz. PDF ve fatura satırlarında `güzergâh (açıklama)` biçiminde gösterilir.
- `pickupAddress`/`deliveryAddress` alanları ilk yükleme ve son teslimat noktasının özetidir (liste, arama, müşteri takibi). Eski istemciler `stops` göndermeden bu iki alanla iş oluşturmaya devam edebilir.
- Yetkili kişiler: `contacts: [{ role, name, phoneNumber }]` (en fazla 10; `role` serbest metin, ör. "Yük sahibi", "Teslim alan"). Güncellemede `contacts` gönderilmezse mevcut liste korunur, boş liste hepsini siler. Yanıtta `contacts` sırayla döner.
- Yanıtta `stops`, `assignedVehicleId`, `assignedVehiclePlate` ve `billingStatus` alanları bulunur. `billingStatus` ayrı bir kolon değildir; işin bağlı olduğu faturanın durumundan türetilir (fatura yok → `NotInvoiced`, Draft/Pending/Failed → `InDraft`, Issued → `Invoiced`).

### Araçlar — `/api/vehicles`
| Metot | Yol | Yetki |
|---|---|---|
| GET | `/` (`driverId`, `isActive`, `search`) | Admin |
| GET | `/my` | Oturum (kullanıcının aktif atanmış araçları) |
| GET | `/{id}` | Admin veya aracın şoförü |
| POST / PUT | `/`, `/{id}` | Admin |
| POST | `/{id}/assign` (`driverId`), `/{id}/unassign` | Admin |

- Araç ekleme, düzenleme ve şoföre atama yalnızca yöneticiye açıktır; şoför araçlarını yalnızca görüntüler.
- Bir şoförün birden fazla aracı olabilir. Bir araç aynı anda yalnızca bir şoföre atanabilir: servis 409 döner, `driver_vehicles(VehicleId) WHERE "IsActive"` filtreli benzersiz indeksi eşzamanlı istekleri de engeller. Atama kaldırıldığında kayıt silinmez, `IsActive=false` olur (geçmiş korunur).
- Plaka büyük harfe ve tek boşluğa normalize edilir ve benzersizdir. Pasif araç yeni işe veya şoföre atanamaz.
- İşe araç atanırken araç aktif olmalı ve seçilen şoföre aktif olarak atanmış olmalıdır; yönetici kendi üstlendiği işte yalnızca kendi araçlarını seçebilir.

### Konum — `/api/locations`
`POST /me` (şoför cihazının konum akışı; iş kimliği gerekmez, şoförün tüm aktif işlerine yazılır, aktif iş yoksa 409), `POST /update`, `GET /jobs/{jobId}/latest`, `GET /jobs/{jobId}/history` (sayfalı, `from`, `to`), `GET /active-jobs`. Müşteri takip bağlantısı: `GET /api/tracking/{token}` (oturumsuz, yalnızca durum ve konum; fiyat içermez).

### Faturalar — `/api/invoices` (Admin)
`GET /`, `GET /{id}`, `POST /`, `PUT /{id}`, `POST /{id}/issue`, `POST /{id}/cancel`, `GET /{id}/pdf`, `GET /{id}/job-report/pdf` (faturadaki işlerin nakliye dökümü), `GET /{id}/jobs`, `POST /{id}/resend-email`, `GET /withholding-types` (KDV tevkifat kodları).

GİB portalı ile imza: `POST /{id}/earchive/sms` (imza SMS'i), `POST /{id}/earchive/sign` (`{operationId, code}`), `POST /{id}/earchive/discard` (imzalanmamış portal taslağını sil), `GET /{id}/earchive/view` (portalın HTML görüntüsü).

Oluşturma/güncelleme isteğinde `withholdingCode` (ör. `"624"`), `noWithholding` ve `iban` alanları vardır. Yanıtta `withholdingAmount`, `payableAmount` (genel toplam − tevkifat), `amountInWords` ("YALNIZ: ... TÜRK LİRASI"), `iban`, `documentNote` ve `requiresSmsSignature` döner.

Bir faturaya aynı müşterinin iptal edilmemiş ve başka faturada olmayan işleri eklenebilir; yönetici işi tamamlanmadan da faturalayabilir (ön fatura / peşin tahsilat). Faturaya eklenen iş yürümeye devam eder ve tamamlanabilir, ancak düzenlenemez ve iptal edilemez. İş, taslak oluşturulurken `transport_jobs.InvoiceId` koşullu güncellemesiyle (yalnızca `InvoiceId IS NULL` satırlar, transaction içinde) faturaya bağlanır; bu yüzden aynı iş eşzamanlı iki faturaya eklenemez. Kesim öncesinde her işin hâlâ bu faturaya bağlı ve iptal edilmemiş olduğu yeniden doğrulanır.

### Nakliye teklifleri — `/api/transport-offers` (Admin)
`GET /` (sayfalı liste; `search` teklif no veya müşteri, `customerId`, `status` (birden çok verilebilir), `startDate`, `endDate`, `page`, `pageSize`), `GET /stats`, `GET /{id}`, `POST /`, `PUT /{id}`, `POST /{id}/copy`, `POST /{id}/send`, `POST /{id}/accept`, `POST /{id}/reject` (`{reason}`), `POST /{id}/cancel` (`{reason}`), `DELETE /{id}`, `GET /{id}/pdf`, `POST /{id}/convert-to-job`. Ayrıntılar: [Nakliye teklifleri](#nakliye-teklifleri).

### Raporlar — `/api/reports` (Admin)
`GET /jobs/pdf`, `GET /jobs/summary`, `GET /jobs` — parametreler: `startDate`, `endDate`, `customerId`, `driverId`, `storeName`, `vehicleType`, `scope` (`Completed` varsayılan, `NotCancelled`, `All`).

### Dashboard — `/api/dashboard` (Admin)
`GET /summary`, `GET /revenue?year=2026&month=10` (aylık nakliye geliri; parametresiz çağrıda içinde bulunulan ay), `GET /daily-jobs?days=30`, `GET /monthly-revenue?months=12`, `GET /recent-jobs?count=10`, `GET /active-drivers`.

Özet alanları: `totalJobs`, `pendingJobs`, `assignedJobs`, `activeJobs` (Accepted + OnTheWay + Loading + Delivering), `completedJobs`, `cancelledJobs`, `totalRevenue` (tamamlanan işlerin tutarı), `monthlyRevenue` (bu ay, iş tarihine göre), `totalCustomers` (aktif), `activeDrivers` (şu an aktif işi olan şoför). Tüm istatistikler veritabanında `GROUP BY`/`SUM` ile hesaplanır.

## İş akışı ve durumlar

```
Pending ──assign──▶ Assigned ──accept──▶ Accepted ──start──▶ OnTheWay ──start-loading──▶ Loading ──start-delivery──▶ Delivering ──complete──▶ Completed
   ▲                   │                    ▲
   └──assign(null)─────┘    claim (Admin) ──┘          Cancel: Completed/Cancelled dışındaki her durumdan (Admin)
```

- Geçişler `TransportJob` entity'sindeki metotlarla yapılır; atlama veya geri dönüş (ör. tamamlanmış işi yeniden yola çıkarma) 409 döner.
- Şoför ataması yalnızca iş başlamadan (Pending/Assigned/Accepted) değiştirilebilir.
- Faturalanmış iş düzenlenemez, silinemez, iptal edilemez.

### İptal ve silme

İptal ve silme ayrı işlemlerdir; silinen iş "iptal" sayılmaz.

| | İptal (`PATCH /{id}/cancel`) | Silme (`DELETE /{id}`) |
|---|---|---|
| Ne olur | Durum `Cancelled` olur, iş listede kalır | `IsDeleted=true`; iş tüm standart sorgulardan çıkar (EF global query filter), durumu değişmez |
| Hangi aşamada | Completed dışındaki her durum | Her durum (iptal edilmiş ve tamamlanmış dahil) |
| Neden | Zorunlu | Devam eden (Accepted, OnTheWay, Loading, Delivering) ve tamamlanmış işte zorunlu |
| Denetim | `CancelledAt`, `CancelledByUserId`, `CancellationReason`, durum geçmişi kaydı | `DeletedAt`, `DeletedByUserId`, `DeletionReason` |
| Engel | Faturaya eklenmiş iş | Faturaya eklenmiş veya daha önce faturada kullanılmış iş |
| Tekrarlanan istek | Mevcut hali döner, ikinci kayıt oluşmaz | Başarılı döner, alanlar değişmez |
| Geri alma | Yok | `PATCH /{id}/restore` |

- Her iki işlemde de şoför ile araç bilgisi geçmiş için korunur; iş artık konum kabul etmez (canlı takip durur). Kayıttan sonra `IJobTrackingStopHandler` kancaları çalışır; varsayılan kanca müşteri takip bağlantılarını kapatır.
- Bildirim: iptalde şoföre ve diğer yöneticilere, silmede (iş kapanmamışsa) şoföre ve diğer yöneticilere. Silinen işe ait eski bildirimler kalır ama işe bağlantı vermez.
- Silinen işin durak, durum geçmişi, konum ve bildirim kayıtları silinmez. Silinmiş işi olan müşteri silinmez, pasife alınır.
- Eşzamanlı iki iptal/silme isteğinde biri kaydeder, diğeri aynı sonucu görüp başarılı döner.
- Her geçiş `job_status_histories` tablosuna önceki durum, yeni durum, işlemi yapan kullanıcı, tarih ve açıklama ile yazılır.

## Konum takibi

- Konum yalnızca işe atanmış kişiden ve yalnızca `Accepted`, `OnTheWay`, `Loading`, `Delivering` durumlarında kabul edilir. Takip şoför işi kabul ettiği anda başlar; tamamlanan veya iptal edilen işe konum eklenemez (409).
- Mobil uygulama arka plan konum akışını `POST /api/locations/me` ile gönderir. Şoförün birden fazla aktif işi varsa aynı konum hepsine yazılır; tek bir GPS akışı yeterlidir.
- Sunucu tarafı filtre: bir önceki kayda göre en az `MinDistanceMeters` (30 m) **ve** `MinIntervalSeconds` (15 sn) fark yoksa kayıt atılmaz; araç dursa bile `HeartbeatSeconds` (120 sn) aralıkla bir kayıt alınır. Doğruluğu `MaxAccuracyMeters` (200 m) üzerindeki, gelecekteki veya 12 saatten eski konumlar reddedilir.
- Çevrimdışı biriken konumlar tek istekte `points` dizisiyle toplu gönderilir (en fazla 50 işlenir) ve tek `SaveChanges` ile yazılır.
- Son konum ayrı `job_latest_locations` tablosunda (iş başına tek satır, PK = JobId) tutulur; harita sorguları büyük geçmiş tablosuna gitmez. Geçmiş için `(JobId, RecordedAt)` index'i ve sayfalama (en fazla 500) vardır.
- Saklama: `LocationTracking__RetentionDays` (varsayılan 90) günden eski konumlar günlük arka plan işiyle silinir.
- **Gerçek zamanlı yayın (SignalR):** hub adresi `/hubs/tracking`. Bağlantı JWT ile doğrulanır; WebSocket'te token `access_token` sorgu parametresiyle gönderilir (yalnızca hub yolunda kabul edilir, istek loglarına sorgu dizesi yazılmaz). Token süresi dolunca sunucu bağlantıyı kapatır, istemci yenilenmiş token ile yeniden bağlanır. Geçici şifresini değiştirmemiş hesaplar bağlanamaz.
  - Gruplar: `admins` (yöneticiler, tüm aktif işler), `user-{id}` (kullanıcının kendisi), `job-{id}` (`WatchJob(jobId)` ile katılınır; sunucu yönetici mi veya işin şoförü mü diye doğrular, değilse `HubException`).
  - Sunucudan gelen olaylar: `locationUpdated` (`LocationDto`: jobId, driverId, latitude, longitude, accuracy, speed, heading, recordedAt) yalnızca `admins` ve ilgili `job-{id}` grubuna; `jobChanged` (`jobId`, `status`, `driverIds`, `removed`) yöneticilere ve işin şu anki/önceki şoförüne gider. `jobChanged` veri taşımaz, istemci ilgili listeyi REST ile yeniden okur.
  - `jobChanged`, `AppDbContext` kaydı başarıyla tamamladıktan sonra iş durumu, şoför/araç ataması değişen, silinen veya soft delete edilen (`IsDeleted`/`DeletedAt` alanı varsa) her iş için otomatik gönderilir; iş servislerine yayın kodu eklenmez.
  - Yayın hatası asıl işlemi bozmaz (`SignalRLocationBroadcaster` loglar). Birden fazla API örneği çalıştırılırsa SignalR için Redis backplane eklenmelidir.
- Müşteri canlı takip sayfası: `wwwroot/takip` (tek HTML + `takip.js`, Leaflet + Esri sokak haritası, gerekirse OpenStreetMap). Yönetici uygulamada iş detayındaki **Canlı konumu paylaş** butonuyla bağlantı üretip WhatsApp/SMS ile gönderir. Sayfa `/api/tracking/{token}` uç noktasını 15 saniyede bir sorgular; iş tamamlanınca veya iptal edilince durur. Bağlantı biçimi `https://<api>/takip/?t=<token>`. `App__TrackingUrl` verilip `{token}` unutulursa token `?t=` olarak eklenir.
- Müşteri takibi: Customer rolü eklenmeden, işe özel süreli ve iptal edilebilir takip token'ı (`job_tracking_tokens`, yalnızca özet saklanır) ile çalışır.

Mobil uygulamadaki `LOCATION_CONFIG` (40 m / 20 sn) ile uyumludur.

## Fatura, e-Arşiv ve e-posta

```
Tamamlanan işler ─▶ POST /api/invoices (Draft) ─▶ POST /{id}/issue ─▶ e-Arşiv sağlayıcısı
                                                        │
                       ┌────────────────────────────────┼─────────────────────────────┐
                 Sağlayıcı yok                  Reddedildi / hata                Kesildi (Issued)
                 503, Draft kalır               Failed (tekrar denenebilir)      aynı transaction'da:
                                                                                 EmailLog + OutboxMessage
                                                                                        │
                                                                    OutboxWorker (arka plan, retry)
                                                                                        │
                                                         e-posta: e-Arşiv PDF + iş dökümü PDF
```

- Fatura oluşturma ile işin tamamlanması bağımsızdır. Bir faturada aynı müşterinin birden fazla işi olabilir; işin bitmiş olması gerekmez.
- Aynı iş iki faturaya eklenemez: `transport_jobs.InvoiceId` koşullu güncellemeyle (`WHERE InvoiceId IS NULL`) transaction içinde bağlanır; eşzamanlı isteklerde biri 409 alır. İptal edilen faturanın işleri serbest kalır.
- Fatura oluştururken müşterinin geçerli e-posta adresi zorunludur.
- **Issued durumuna yalnızca gerçek e-Arşiv sağlayıcısının başarılı yanıtıyla geçilir.** Sağlayıcı yapılandırılmamışsa `issue` 503 döner ve fatura Draft kalır. Taslak faturanın PDF'i "TASLAK – resmi fatura değildir" filigranlıdır; yanıtta `isOfficial: false`.
- Sağlayıcı faturayı asenkron işliyorsa fatura `Pending` kalır; `EArchiveStatusSyncWorker` 5 dakikada bir durumu sorgular.
- Gönderim yarıda kalırsa (API durdu, bağlantı koptu) fatura sağlayıcı referansı olmadan `Pending` kalır. Aynı worker bu faturaları 5 dakika sonra `Failed` işaretler; fatura yeniden gönderilebilir. Bu sürede tekrar `issue` çağrısı 409 döner. GİB portalında imzasız fazladan taslak oluşmuşsa portaldan silinmelidir.
- **Transactional outbox:** Fatura `Issued` olduğu anda `email_logs` ve `outbox_messages` kayıtları aynı `SaveChanges` içinde yazılır. HTTP isteği e-posta göndermeyi beklemez.
- `OutboxWorker` mesajı koşullu UPDATE ile kilitler (çoklu sunucuda tek işleyici), üstel geri çekilmeyle (30 sn, 60 sn, 120 sn …) en fazla 5 kez dener, sonra `Failed` işaretler. `POST /api/invoices/{id}/resend-email` başarısız gönderimi yeniden kuyruğa alır.
- Mail başarısız olsa da fatura durumu değişmez ve e-Arşiv'e yeniden gönderilmez.
- Mükerrer gönderim: fatura başına tek `EmailLog` (unique index) ve gönderilmiş kayıt tekrar gönderilmez. (Not: SMTP sunucusu maili kabul ettikten hemen sonra veritabanı yazılamazsa en kötü durumda bir kez daha gönderilebilir; outbox "en az bir kez" garantisi verir.)
- E-posta konusu: `AKNA Transport - Faturanız ve Nakliye İş Dökümünüz`. Ekler: resmi e-Arşiv PDF'i ve yalnızca o faturadaki işleri içeren iş dökümü PDF'i.

### Tevkifat, yazıyla tutar ve IBAN

- KDV tevkifatı fatura başına seçilir. Oran KDV'nin yüzdesidir: 624 Yük taşımacılığı **2/10** → 4.000 TL + 800 TL KDV için 160 TL tevkifat, ödenecek tutar 4.640 TL. Kodlar `Domain/Common/Withholding.cs` içindedir (KDV Genel Uygulama Tebliği I/C-2.1.3 kısmi tevkifat kodları).
- Faturanın açıklaması (PDF'te "AÇIKLAMA" bölümü, e-Arşiv'de `not` alanı) şunları içerir: ödenecek tutarın yazıyla gösterimi (`YALNIZ: DÖRT BİN ALTI YÜZ KIRK TÜRK LİRASI`), tevkifat satırı, IBAN (banka adıyla) ve kullanıcı notu.
- IBAN fatura oluşturulurken faturaya kaydedilir (sonradan ayar değişse de eski faturalar değişmez) ve mod-97 ile doğrulanır.

### GİB e-Arşiv portalı (`EArchive__Provider=GibPortal`)

Portal çağrıları açık kaynak [f/fatura](https://github.com/f/fatura) kütüphanesindeki akışı temel alır; tevkifat alanları (`faturaTipi: TEVKIFAT`, satırda `tevkifatKodu` ve vergi kodu 9015 `V9015Orani/Tutari`) portalın kendi JSON anahtarlarıdır.

```
POST /{id}/issue ─▶ portal girişi ─▶ taslak oluştur (EARSIV_PORTAL_FATURA_OLUSTUR) ─▶ Pending (requiresSmsSignature)
POST /{id}/earchive/sms ─▶ kayıtlı yetkili telefonuna SMS ─▶ { operationId, maskedPhone }
POST /{id}/earchive/sign ─▶ kod doğrulama + imza ─▶ portal "Onaylandı" gösterirse Issued (+ e-posta kuyruğu)
```

- Fatura **yalnızca GİB imzayı onaylayıp belge portalda "Onaylandı" görünürse** `Issued` olur. Yanlış kodda fatura `Pending` kalır; aynı SMS ile tekrar denenebilir (10 dk).
- ETTN'i GİB üretir; adaptör oluşturma öncesi/sonrası taslak listesini karşılaştırıp yeni belgeyi alıcı VKN'si ile eşleştirir.
- Müşterinin 10 haneli VKN'si veya 11 haneli TCKN'si kayıtlı olmalıdır; yoksa portala istek gitmeden fatura `Failed` olur.
- İmza portalın kendi ekranından yapılırsa `EArchiveStatusSyncWorker` durumu bulur ve faturayı `Issued` yapar.
- `POST /{id}/earchive/discard` imzalanmamış taslağı portaldan siler, fatura tekrar düzenlenebilir `Draft` olur.
- İmzalı faturanın iptali portalda **iptal talebi** olarak oluşturulur (`EARSIV_PORTAL_IPTAL_TALEBI_OLUSTUR`); alıcının onayı veya yasal süre ile kesinleşir.
- Portal PDF vermez: kesilmiş faturanın PDF'i GİB fatura numarası ve ETTN ile API'de üretilir; portalın resmi görüntüsü `GET /{id}/earchive/view` ile alınır.
- SMS oturumu bellek içi önbellekte tutulur; API birden fazla sunucuda çalışıyorsa SMS gönderme ve imza isteği aynı sunucuya gitmelidir (sticky session).
- GİB, imza komutunu zaman zaman opak bir kimlikle değiştirir; değişirse `EArchive__GibSmsVerifyCommand` ile kod değiştirmeden güncellenir.
- Alıcı e-Fatura mükellefiyse GİB e-Arşiv faturasını kabul etmez; bu alıcılar için e-Fatura entegratörü gerekir.

### Yeni e-Arşiv sağlayıcısı ekleme

1. `Infrastructure/EArchive` altında `HttpEArchiveProviderAdapterBase`'den türeyen bir sınıf yazın (ör. `UyumsoftEArchiveAdapter`). `Name`, `SendInvoiceAsync`, `GetStatusAsync`, `GetPdfAsync`, `CancelAsync` metotlarını sağlayıcı dokümantasyonuna göre uygulayın (belge `EArchiveDocumentKey` ile — referans + fatura tarihi — bulunur). SMS ile imza gerektiren sağlayıcılar `IEArchiveSmsSigningAdapter` uygular. `EArchiveInvoiceRequest.InvoiceId` belge UUID'si (ETTN) olarak kullanılabilir; böylece tekrar denemelerde mükerrer belge oluşmaz.
2. `Infrastructure/DependencyInjection.cs` içinde `services.AddEArchiveProvider<UyumsoftEArchiveAdapter>();` ekleyin.
3. `EArchive__Provider=Uyumsoft` ve kimlik bilgilerini environment variable olarak verin.

Sahte (fake) bir sağlayıcı üretim koduna eklenmemiştir. Testlerdeki sahte adaptör yalnızca test projesindedir.

## SMS ile kullanıcı şifresi

- Yönetici kullanıcı eklediğinde veya `POST /api/users/{id}/reset-password` çağırdığında sistem 10 karakterlik rastgele geçici şifre üretir (karışan karakterler hariç) ve `MustChangePassword=true` işaretler.
- SMS sağlayıcısı yapılandırılmışsa şifre outbox üzerinden SMS ile gönderilir; mesaj işlendikten sonra outbox içeriği silinir. Kullanıcı şifresini değiştirmişse eski geçici şifre gönderilmez.
- SMS sağlayıcısı **yapılandırılmamışsa** sahte gönderim yapılmaz; geçici şifre yalnızca yöneticiye dönen yanıtta (`temporaryPassword`) bir kez gösterilir, hiçbir yerde saklanmaz veya loglanmaz.
- Yeni SMS sağlayıcısı: `ISmsProviderAdapter` uygulayıp `services.AddSmsProvider<NetgsmSmsAdapter>()` ile kaydedin ve `Sms__Provider=Netgsm` verin. Telefonlar `905XXXXXXXXX` biçimine normalize edilir.

## Bildirimler

İş olaylarında ilgili kişiye bildirim oluşturulur. Bildirim kaydı iş değişikliğiyle aynı transaction'da yazılır; telefon bildirimi (push) outbox üzerinden Expo Push API ile gönderilir. İşlemi yapan kişiye bildirim gitmez.

| Olay | Kime | Başlık |
|---|---|---|
| İş oluşturulurken şoför seçildi / şoför atandı | Yeni şoför | Yeni iş atandı |
| Şoför değişti veya atama kaldırıldı, yönetici işi üstlendi | Eski şoför | İş sizden alındı |
| Atanmış işin bilgileri düzenlendi | Şoför | İş bilgileri güncellendi |
| İş iptal edildi / silindi | Şoför ve diğer aktif yöneticiler | İş iptal edildi / İş kaldırıldı, İş silindi |
| Şoför kabul etti, yola çıktı, yüklemeye başladı, teslimata çıktı, tamamladı | Tüm aktif yöneticiler | İş kabul edildi, Şoför yola çıktı, Yükleme başladı, Teslimata çıkıldı, İş tamamlandı |

`/api/notifications` (giriş yapmış kullanıcı, yalnızca kendi bildirimleri):

| Metot | Yol | Açıklama |
|---|---|---|
| GET | `/` | Bildirimler, en yenisi önce (`unreadOnly=true`, sayfalama) |
| GET | `/unread-count` | Okunmamış sayısı |
| POST | `/{id}/read` | Okundu işaretle |
| POST | `/read-all` | Hepsini okundu işaretle |
| POST | `/devices` | Telefonun Expo push token'ını kaydet (`{ token, platform }`) |
| POST | `/devices/remove` | Çıkışta token'ı sil (`{ token }`) |

- Push içeriği: başlık, metin, `data = { notificationId, type, jobId }`, iOS rozet sayısı = okunmamış bildirim sayısı. Android kanal adı `jobs`.
- Aynı token başka kullanıcıyla kaydedilirse son giriş yapan kullanıcıya taşınır. Expo `DeviceNotRegistered` dönerse token silinir.
- Okunmuş veya pasif kullanıcıya ait bildirim telefona gönderilmez. Bildirimler 90 gün saklanır (`BackgroundJobs__NotificationRetentionDays`).

## PDF iş dökümü

`GET /api/reports/jobs/pdf?startDate=2026-10-19&endDate=2026-10-22`

- A4 yatay, siyah-beyaz, Türkçe karakter destekli (QuestPDF gömülü Lato fontu).
- Başlık: "AKNA TRANSPORT / NAKLİYE İŞLERİ DÖKÜMÜ", dönem ve iş sayısı; başlık ve tablo başlığı her sayfada tekrarlanır.
- Sütunlar: Tarih, Mağaza / Firma, Araç, Yapılan Nakliyeler, Fiyat (sağa hizalı, `36.500,00 TL`).
- Alt bölüm: toplam iş sayısı ve toplam nakliye tutarı; footer'da oluşturulma tarihi (İstanbul saati), kurumsal satır ve "Sayfa x / y".
- Varsayılan yalnızca tamamlanan işler; `scope=NotCancelled` veya `scope=All` ile diğer durumlar.
- PDF üretimi Infrastructure katmanındadır (`QuestPdfJobReportGenerator`).

## Nakliye teklifleri

Müşteriye verilen fiyat teklifleri; kabul edilen teklif tek tıkla nakliye işine dönüştürülür.

- **Numara:** `AKT-YYYY-NNNNNN` (PostgreSQL `offer_number_seq`, benzersiz index). `AKN-` öneki nakliye işlerinde kullanıldığı için tekliflerde `AKT-` kullanılır.
- **Durumlar:** `Draft` (taslak) → `Pending` (müşteriye gönderildi) → `Accepted` / `Rejected`; ayrıca `Cancelled`. `Expired` saklanmaz, okuma sırasında türetilir: `Draft` veya `Pending` teklifin `validUntil` tarihi (İstanbul saatine göre) geçtiyse `Expired` döner. Süresi dolan teklif düzenlenip geçerlilik tarihi uzatılırsa yeniden açılır. Yanıttaki `storedStatus` veritabanındaki ham durumu verir.
- **Geçişler:** gönder yalnızca taslaktan (beklemedeki teklifte etkisizdir); kabul/ret yalnızca taslak veya beklemedeki, süresi dolmamış teklifte; iptal reddedilmemiş ve işe dönüştürülmemiş teklifte; silme (soft delete) kabul edilmemiş ve dönüştürülmemiş teklifte. Geçersiz geçiş `409` döner. Kabul edilen teklif kilitlenir; değiştirmek için kopyalanır. Her geçiş `transport_offer_histories` tablosuna kullanıcı ve zamanla yazılır.
- **Tutarlar sunucuda hesaplanır:** kalem toplamı = miktar × birim fiyat (2 hane), ara toplam − iskonto = net, KDV = net × oran, genel toplam = net + KDV. İstemcinin gönderdiği toplamlar dikkate alınmaz. İskonto ara toplamdan küçük olmalıdır. Para birimi yalnızca `TRY` (işler ve faturalar TL olduğu için).
- **Müşteri:** `customerId` ile mevcut müşteri seçilir ya da `customer` nesnesiyle aynı işlemde yeni müşteri oluşturulur. Müşteri adı, yetkili, telefon, e-posta ve adres teklife kopyalanır; müşteri kaydı sonradan değişse de teklif ve PDF ilk hâliyle kalır.
- **PDF:** `GET /{id}/pdf`, dosya adı `AKNA_Teklif_AKT-2026-000125.pdf`. A4 dikey, siyah-beyaz, logo, firma ve müşteri bilgileri, güzergâh, kalemler, iskonto/KDV/genel toplam ve yazıyla tutar, teklif koşulları, imza alanları. `internalNotes` PDF'e yazılmaz; `customerNotes` yazılır. Üretim `QuestPdfTransportOfferGenerator` (Infrastructure).
- **İşe dönüştürme:** `POST /{id}/convert-to-job` yalnızca kabul edilen tekliflerde çalışır ve tek transaction içinde mevcut `JobService.CreateAsync` ile iş oluşturur (müşteri, araç tipi, yükleme/teslim noktaları, iş tarihi = yükleme tarihi ya da bugün, fiyat = KDV hariç net tutar, notlarda teklif no ve koşullar). Teklif satırı önce koşullu `UPDATE` ile kilitlenir (`Status = Accepted AND ConvertedJobId IS NULL`); eşzamanlı ikinci istek `409` alır, `ConvertedJobId` üzerindeki benzersiz index de aynı tekliften ikinci işi engeller. İş oluşturma hata verirse teklif değişmez.

## Mobil uygulama entegrasyonu

Mobil uygulama `EXPO_PUBLIC_API_BASE_URL` tanımlandığında bu API'yi kullanır (`src/services/api/*`). Değişken boşsa cihaz üzerindeki demo verilerle çalışır. Yerelde:

```bash
# API (Development, port 5080). Telefon/emülatörden erişilecekse tüm arayüzlerde dinletin:
dotnet run --project AknaTransportApi.Api --urls http://0.0.0.0:5080

# Mobil uygulama (ayrı terminal, AknaTransportNative kökünde)
echo "EXPO_PUBLIC_API_BASE_URL=http://localhost:5080" > .env.local   # web / iOS simülatör
# Android emülatör: http://10.0.2.2:5080 · Gerçek cihaz: http://<bilgisayarın-LAN-IP'si>:5080
npx expo start
```

Development'ta CORS tüm kaynaklara açıktır; üretimde `Cors__AllowedOrigins__0` ile web adresi tanımlanmalıdır. Fatura listesi (`GET /api/invoices`) mobil liste ekranı için `firstJobId`, `firstJobNumber`, `description` ve `customerEmail` alanlarını da döner.

### Enum değerleri

Backend enum değerleri İngilizce ve sabittir, JSON'da string döner. Frontend kendi Türkçe etiketlerine çevirir.

| Frontend (`types/index.ts`) | API |
|---|---|
| `pending` / `assigned` / `accepted` | `Pending` / `Assigned` / `Accepted` |
| `on_the_way` / `loading` / `delivering` | `OnTheWay` / `Loading` / `Delivering` |
| `completed` / `cancelled` | `Completed` / `Cancelled` |
| `panelvan` / `small_panelvan` / `pickup` / `truck` / `other` | `Panelvan` / `SmallPanelvan` / `Pickup` / `Truck` / `Other` |
| `UserRole`: `admin` / `driver` | `Admin` / `User` |
| `InvoiceStatus`: `demo_draft`, `demo_created` | `Draft` (`isOfficial: false`) |
| `earsiv_pending` / `earsiv_issued` / `earsiv_failed` / `cancelled` | `Pending` / `Issued` / `Failed` / `Cancelled` |
| `InvoiceEmailStatus`: `queued` / `sent` / `failed` | `email.status`: `Pending` / `Sent` / `Failed` (kayıt yoksa `not_sent`) |

### Alan eşleşmeleri

| Model | Frontend | API |
|---|---|---|
| User | `phone` | `phoneNumber` |
| User | — | `mustChangePassword` (true ise şifre değiştirme ekranı) |
| Customer | `contactName`, `phone`, `billingAddress`, `notes` | `contactPerson`, `phoneNumber`, `address`, `description` |
| Customer | — | `isActive` |
| TransportJob | `jobNo`, `date`, `driverId`, `createdBy` | `jobNumber`, `jobDate`, `assignedDriverId`, `createdByUserId` |
| TransportJob | `pickupPoint` / `deliveryPoint` | `pickupLatitude`/`pickupLongitude`, `deliveryLatitude`/`deliveryLongitude` |
| TransportJob | `statusHistory[]` (`status`, `at`, `byUserId`, `note`) | `GET /api/jobs/{id}` → `statusHistory[]` (`toStatus`, `changedAt`, `changedByUserId`, `description`, `fromStatus`) |
| TransportJob | — | `customerName`, `assignedDriverName`, `invoiceNumber`, `currency`, `startedAt`, `completedAt` |
| Invoice | `invoiceNo`, `issueDate`, `subtotal`, `total` | `invoiceNumber`, `invoiceDate`, `subTotal`, `grandTotal` |
| Invoice | `jobId`, `jobNo` (tek iş) | `items[]` (çoklu iş: `jobId`, `jobNumber`, …) |
| Invoice | `ettn`, `externalId` | `eArchiveReference`, `eArchiveInvoiceNumber` |
| Invoice | `mode: 'demo' \| 'earsiv'` | `isOfficial` |
| LocationFix | `timestamp` (ms) | `recordedAt` (ISO 8601 UTC) |
| VehicleLocation | `{ jobId, driverId, fix }` | `ActiveJobLocationDto { jobId, driverId, location: { latitude, longitude, accuracy, speed, heading, recordedAt } }` |
| Session | `token` | `accessToken`, `refreshToken`, `accessTokenExpiresAt` |

Servis eşleşmeleri:
- `jobService.takeOver` → `PATCH /api/jobs/{id}/claim`; `advance(id, next)` → `next` değerine göre `accept`/`start`/`start-loading`/`start-delivery`/`complete`.
- `jobService.assign(id, null)` → `PATCH /api/jobs/{id}/assign` `{ "driverId": null }`.
- `invoiceService.createDemo` → `POST /api/invoices` (Draft); gerçek kesim `POST /api/invoices/{id}/issue`.
- `eArchiveGateway` ve `invoiceMailer` frontend'de çağrılmaz; backend yönetir.
- `locationRepository.publish` → `POST /api/locations/update`; `listActive` → `GET /api/locations/active-jobs`.
- `userService.list` → `GET /api/users?role=User&isActive=true`.
- Mobil uygulamadaki `Vehicle` (plaka) modeli için backend'de henüz tablo yoktur; araç tipi iş üzerinde tutulur.

CORS: Native mobil uygulama CORS'a tabi değildir. Expo web veya bir yönetim paneli için `Cors__AllowedOrigins__0=https://...` tanımlayın. Development'ta tüm origin'lere izin verilir. `Content-Disposition` başlığı PDF dosya adı için dışarı açıktır.

## Testler

```bash
dotnet restore
dotnet build
dotnet test
```

Integration testleri `WebApplicationFactory` ile API'yi geçici bir SQLite veritabanıyla ayağa kaldırır (PostgreSQL gerekmez). Kapsanan senaryolar: admin ve şoför girişi, açık kaydın engellenmesi, geçici şifre zorunluluğu, refresh token rotation ve tekrar kullanım tespiti, yetkisiz iş oluşturma, admin'in işi kendine ataması ve şoför gibi tamamlaması, şoförün yalnızca kendi işlerini görmesi, başka şoförün işini/konumunu değiştirememe, tamamlanmış işin yeniden başlatılamaması, konum filtresi, aynı işin iki faturaya eklenememesi, gerçek e-Arşiv yanıtı olmadan Issued olmaması, başarılı faturada e-postanın kuyruğa alınması, mail hatasında faturanın yeniden kesilmemesi, aynı mailin iki kez gönderilmemesi, PDF raporunda tarih aralığı ve durum kapsamı, dashboard ve müşteri pasife alma, çoklu durak ve güzergâh açıklaması, müşteri bazlı adres geçmişi, şoföre ait olmayan/pasif aracın işe atanamaması, aracın iki şoföre atanamaması, kabulle başlayan ve iş kapanınca duran konum takibi, faturalandırma durumu (NotInvoiced → InDraft → Invoiced), fatura döküm PDF'i ve aylık gelir (iptal ve bekleyen işler hariç).

Not: Proje `net9.0` hedefler. Yalnızca .NET 10 SDK/runtime kurulu bir makinede testleri çalıştırmak için `DOTNET_ROLL_FORWARD=Major dotnet test` kullanılabilir.

## Gerçek sağlayıcı bilgisi bekleyen entegrasyonlar

| Entegrasyon | Durum | Gerekenler |
|---|---|---|
| e-Arşiv faturası | Adapter altyapısı hazır, somut sağlayıcı yok. Fatura Draft'ta kalır. | Entegratör seçimi (Uyumsoft, Logo, Sovos, QNB eFinans vb.), API dokümantasyonu, test hesabı, `EArchive__*` değerleri |
| SMS | Adapter altyapısı hazır, somut sağlayıcı yok. Geçici şifre yöneticiye bir kez gösterilir. | SMS firması (ör. Netgsm, İleti Merkezi), API bilgileri, onaylı SMS başlığı |
| E-posta (Resend / SMTP) | Resend HTTP API'si veya MailKit SMTP ile çalışır durumda. Müşterinin ek adresleri CC olarak eklenir. Development'ta (SMTP modunda) `mail-drop/` klasörüne `.eml` yazılır. | `Email__Provider=Resend` + `Email__ResendApiKey` + doğrulanmış alan adındaki `Email__FromEmail` |
| Gerçek zamanlı konum (SignalR) | Genişleme noktası hazır (`ILocationBroadcaster`), ilk aşamada zorunlu değil. | — |
