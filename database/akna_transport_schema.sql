-- AKNA Transport veritabanı şeması (PostgreSQL 14+)
-- EF Core migration'larından üretilmiştir; tekrar çalıştırmak güvenlidir (idempotent).
-- Yeniden üretmek için: dotnet ef migrations script --idempotent -p src/AknaTransport.Infrastructure -s src/AknaTransport.Api -o database/akna_transport_schema.sql
-- Uygulama: psql -h localhost -U akna -d akna_transport -f database/akna_transport_schema.sql
-- Not: Kullanıcı/örnek veri bu betikte yoktur. İlk yönetici ve (yalnızca Development) örnek veriler API ilk açılışta oluşturur.

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE SEQUENCE invoice_number_seq START WITH 1 INCREMENT BY 1 NO CYCLE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE SEQUENCE job_number_seq START WITH 1 INCREMENT BY 1 NO CYCLE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE customers (
        "Id" uuid NOT NULL,
        "CompanyName" character varying(200) NOT NULL,
        "ContactPerson" character varying(150),
        "PhoneNumber" character varying(30),
        "Email" character varying(200),
        "TaxOffice" character varying(100),
        "TaxNumber" character varying(11),
        "Address" character varying(500),
        "Description" character varying(1000),
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        CONSTRAINT "PK_customers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE outbox_messages (
        "Id" uuid NOT NULL,
        "Type" character varying(100) NOT NULL,
        "Payload" text NOT NULL,
        "Status" character varying(20) NOT NULL,
        "AttemptCount" integer NOT NULL,
        "NextAttemptAt" timestamp with time zone NOT NULL,
        "LockedUntil" timestamp with time zone,
        "LastError" character varying(2000),
        "CreatedAt" timestamp with time zone NOT NULL,
        "ProcessedAt" timestamp with time zone,
        CONSTRAINT "PK_outbox_messages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE users (
        "Id" uuid NOT NULL,
        "FullName" character varying(150) NOT NULL,
        "Email" character varying(200) NOT NULL,
        "NormalizedEmail" character varying(200) NOT NULL,
        "PhoneNumber" character varying(30) NOT NULL,
        "PasswordHash" character varying(300) NOT NULL,
        "Role" character varying(20) NOT NULL,
        "IsActive" boolean NOT NULL,
        "MustChangePassword" boolean NOT NULL,
        "PasswordResetTokenHash" character varying(128),
        "PasswordResetTokenExpiresAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        CONSTRAINT "PK_users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE invoices (
        "Id" uuid NOT NULL,
        "InvoiceNumber" character varying(30) NOT NULL,
        "CustomerId" uuid NOT NULL,
        "InvoiceDate" date NOT NULL,
        "SubTotal" numeric(18,2) NOT NULL,
        "VatRate" numeric(5,2) NOT NULL,
        "VatAmount" numeric(18,2) NOT NULL,
        "GrandTotal" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "Status" character varying(20) NOT NULL,
        "Notes" character varying(2000),
        "EArchiveReference" character varying(100),
        "EArchiveInvoiceNumber" character varying(50),
        "EArchiveInvoiceUrl" character varying(1000),
        "EArchiveProvider" character varying(50),
        "InvoicePdfPath" character varying(300),
        "LastError" character varying(2000),
        "CreatedByUserId" uuid NOT NULL,
        "IssuedAt" timestamp with time zone,
        "CancelledAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "ConcurrencyStamp" uuid NOT NULL,
        CONSTRAINT "PK_invoices" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_invoices_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES customers ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_invoices_users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE refresh_tokens (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedByIp" character varying(64),
        "RevokedAt" timestamp with time zone,
        "RevokedReason" character varying(200),
        "ReplacedByTokenId" uuid,
        CONSTRAINT "PK_refresh_tokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_refresh_tokens_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE email_logs (
        "Id" uuid NOT NULL,
        "InvoiceId" uuid,
        "Type" character varying(30) NOT NULL,
        "RecipientEmail" character varying(200) NOT NULL,
        "Subject" character varying(300) NOT NULL,
        "Status" character varying(20) NOT NULL,
        "AttemptCount" integer NOT NULL,
        "LastError" character varying(2000),
        "SentAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_email_logs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_email_logs_invoices_InvoiceId" FOREIGN KEY ("InvoiceId") REFERENCES invoices ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE transport_jobs (
        "Id" uuid NOT NULL,
        "JobNumber" character varying(30) NOT NULL,
        "CustomerId" uuid NOT NULL,
        "CreatedByUserId" uuid NOT NULL,
        "AssignedDriverId" uuid,
        "JobDate" date NOT NULL,
        "StoreName" character varying(200) NOT NULL,
        "VehicleType" character varying(20) NOT NULL,
        "PickupAddress" character varying(500) NOT NULL,
        "DeliveryAddress" character varying(500) NOT NULL,
        "PickupLatitude" double precision,
        "PickupLongitude" double precision,
        "DeliveryLatitude" double precision,
        "DeliveryLongitude" double precision,
        "Description" character varying(1000) NOT NULL,
        "Price" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "Status" character varying(20) NOT NULL,
        "Notes" character varying(2000),
        "CancellationReason" character varying(500),
        "InvoiceId" uuid,
        "StartedAt" timestamp with time zone,
        "CompletedAt" timestamp with time zone,
        "CancelledAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "ConcurrencyStamp" uuid NOT NULL,
        CONSTRAINT "PK_transport_jobs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_transport_jobs_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES customers ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_transport_jobs_invoices_InvoiceId" FOREIGN KEY ("InvoiceId") REFERENCES invoices ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_transport_jobs_users_AssignedDriverId" FOREIGN KEY ("AssignedDriverId") REFERENCES users ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_transport_jobs_users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE invoice_items (
        "Id" uuid NOT NULL,
        "InvoiceId" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "Description" character varying(500) NOT NULL,
        "Quantity" numeric(18,3) NOT NULL,
        "UnitPrice" numeric(18,2) NOT NULL,
        "VatRate" numeric(5,2) NOT NULL,
        "LineTotal" numeric(18,2) NOT NULL,
        CONSTRAINT "PK_invoice_items" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_invoice_items_invoices_InvoiceId" FOREIGN KEY ("InvoiceId") REFERENCES invoices ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_invoice_items_transport_jobs_JobId" FOREIGN KEY ("JobId") REFERENCES transport_jobs ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE job_latest_locations (
        "JobId" uuid NOT NULL,
        "DriverId" uuid NOT NULL,
        "Latitude" double precision NOT NULL,
        "Longitude" double precision NOT NULL,
        "Accuracy" double precision,
        "Speed" double precision,
        "Heading" double precision,
        "RecordedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_job_latest_locations" PRIMARY KEY ("JobId"),
        CONSTRAINT "FK_job_latest_locations_transport_jobs_JobId" FOREIGN KEY ("JobId") REFERENCES transport_jobs ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE job_status_histories (
        "Id" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "FromStatus" character varying(20),
        "ToStatus" character varying(20) NOT NULL,
        "ChangedByUserId" uuid NOT NULL,
        "ChangedAt" timestamp with time zone NOT NULL,
        "Description" character varying(1000),
        CONSTRAINT "PK_job_status_histories" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_job_status_histories_transport_jobs_JobId" FOREIGN KEY ("JobId") REFERENCES transport_jobs ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_job_status_histories_users_ChangedByUserId" FOREIGN KEY ("ChangedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE job_tracking_tokens (
        "Id" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedByUserId" uuid NOT NULL,
        "RevokedAt" timestamp with time zone,
        CONSTRAINT "PK_job_tracking_tokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_job_tracking_tokens_transport_jobs_JobId" FOREIGN KEY ("JobId") REFERENCES transport_jobs ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE TABLE locations (
        "Id" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "DriverId" uuid NOT NULL,
        "Latitude" double precision NOT NULL,
        "Longitude" double precision NOT NULL,
        "Accuracy" double precision,
        "Speed" double precision,
        "Heading" double precision,
        "RecordedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_locations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_locations_transport_jobs_JobId" FOREIGN KEY ("JobId") REFERENCES transport_jobs ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_locations_users_DriverId" FOREIGN KEY ("DriverId") REFERENCES users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_customers_CompanyName" ON customers ("CompanyName");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_customers_IsActive" ON customers ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_customers_TaxNumber" ON customers ("TaxNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_email_logs_InvoiceId" ON email_logs ("InvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_email_logs_Status_CreatedAt" ON email_logs ("Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_invoice_items_InvoiceId_JobId" ON invoice_items ("InvoiceId", "JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_invoice_items_JobId" ON invoice_items ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_invoices_CreatedByUserId" ON invoices ("CreatedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_invoices_CustomerId_InvoiceDate" ON invoices ("CustomerId", "InvoiceDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_invoices_EArchiveReference" ON invoices ("EArchiveReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_invoices_InvoiceNumber" ON invoices ("InvoiceNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_invoices_Status_InvoiceDate" ON invoices ("Status", "InvoiceDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_job_latest_locations_DriverId" ON job_latest_locations ("DriverId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_job_status_histories_ChangedByUserId" ON job_status_histories ("ChangedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_job_status_histories_JobId_ChangedAt" ON job_status_histories ("JobId", "ChangedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_job_tracking_tokens_JobId" ON job_tracking_tokens ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_job_tracking_tokens_TokenHash" ON job_tracking_tokens ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_locations_DriverId" ON locations ("DriverId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_locations_JobId_RecordedAt" ON locations ("JobId", "RecordedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_locations_RecordedAt" ON locations ("RecordedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_outbox_messages_Status_NextAttemptAt" ON outbox_messages ("Status", "NextAttemptAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_refresh_tokens_ExpiresAt" ON refresh_tokens ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_refresh_tokens_TokenHash" ON refresh_tokens ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_refresh_tokens_UserId_RevokedAt" ON refresh_tokens ("UserId", "RevokedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_transport_jobs_AssignedDriverId_Status" ON transport_jobs ("AssignedDriverId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_transport_jobs_CreatedByUserId" ON transport_jobs ("CreatedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_transport_jobs_CustomerId_JobDate" ON transport_jobs ("CustomerId", "JobDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_transport_jobs_InvoiceId" ON transport_jobs ("InvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_transport_jobs_JobDate" ON transport_jobs ("JobDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_transport_jobs_JobNumber" ON transport_jobs ("JobNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_transport_jobs_Status_JobDate" ON transport_jobs ("Status", "JobDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_users_NormalizedEmail" ON users ("NormalizedEmail");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    CREATE INDEX "IX_users_Role_IsActive" ON users ("Role", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031184658_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261031184658_InitialCreate', '9.0.10');
    END IF;
END $EF$;
COMMIT;

