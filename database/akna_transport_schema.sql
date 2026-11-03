-- AKNA Transport veritabanı şeması (PostgreSQL 14+)
-- EF Core migration'larından üretilmiştir; tekrar çalıştırmak güvenlidir (idempotent).
-- Yeniden üretmek için: dotnet ef migrations script --idempotent -p AknaTransportApi.Infrastructure -s AknaTransportApi.Infrastructure -o database/akna_transport_schema.sql
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

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    ALTER TABLE transport_jobs ADD "AssignedVehicleId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    ALTER TABLE transport_jobs ADD "RouteDescription" character varying(2000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    CREATE TABLE transport_job_stops (
        "Id" uuid NOT NULL,
        "TransportJobId" uuid NOT NULL,
        "StopType" character varying(20) NOT NULL,
        "Address" character varying(500) NOT NULL,
        "Latitude" double precision,
        "Longitude" double precision,
        "Sequence" integer NOT NULL,
        "Description" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_transport_job_stops" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_transport_job_stops_sequence" CHECK ("Sequence" > 0),
        CONSTRAINT "FK_transport_job_stops_transport_jobs_TransportJobId" FOREIGN KEY ("TransportJobId") REFERENCES transport_jobs ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    CREATE TABLE vehicles (
        "Id" uuid NOT NULL,
        "PlateNumber" character varying(20) NOT NULL,
        "Brand" character varying(100),
        "Model" character varying(100),
        "VehicleType" character varying(20) NOT NULL,
        "IsActive" boolean NOT NULL,
        "Description" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        CONSTRAINT "PK_vehicles" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    CREATE TABLE driver_vehicles (
        "Id" uuid NOT NULL,
        "DriverId" uuid NOT NULL,
        "VehicleId" uuid NOT NULL,
        "IsActive" boolean NOT NULL,
        "AssignedAt" timestamp with time zone NOT NULL,
        "AssignedByUserId" uuid NOT NULL,
        "UnassignedAt" timestamp with time zone,
        CONSTRAINT "PK_driver_vehicles" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_driver_vehicles_users_AssignedByUserId" FOREIGN KEY ("AssignedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_driver_vehicles_users_DriverId" FOREIGN KEY ("DriverId") REFERENCES users ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_driver_vehicles_vehicles_VehicleId" FOREIGN KEY ("VehicleId") REFERENCES vehicles ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    CREATE INDEX "IX_transport_jobs_AssignedVehicleId" ON transport_jobs ("AssignedVehicleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    CREATE INDEX "IX_driver_vehicles_AssignedByUserId" ON driver_vehicles ("AssignedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    CREATE INDEX "IX_driver_vehicles_DriverId_IsActive" ON driver_vehicles ("DriverId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    CREATE UNIQUE INDEX "IX_driver_vehicles_VehicleId_active" ON driver_vehicles ("VehicleId") WHERE "IsActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    CREATE UNIQUE INDEX "IX_transport_job_stops_TransportJobId_StopType_Sequence" ON transport_job_stops ("TransportJobId", "StopType", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    INSERT INTO transport_job_stops ("Id", "TransportJobId", "StopType", "Address", "Latitude", "Longitude", "Sequence", "Description", "CreatedAt")
    SELECT gen_random_uuid(), j."Id", 'Pickup', j."PickupAddress", j."PickupLatitude", j."PickupLongitude", 1, NULL, j."CreatedAt"
    FROM transport_jobs j
    WHERE btrim(j."PickupAddress") <> '';

    INSERT INTO transport_job_stops ("Id", "TransportJobId", "StopType", "Address", "Latitude", "Longitude", "Sequence", "Description", "CreatedAt")
    SELECT gen_random_uuid(), j."Id", 'Delivery', j."DeliveryAddress", j."DeliveryLatitude", j."DeliveryLongitude", 1, NULL, j."CreatedAt"
    FROM transport_jobs j
    WHERE btrim(j."DeliveryAddress") <> '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    CREATE INDEX "IX_vehicles_IsActive" ON vehicles ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    CREATE UNIQUE INDEX "IX_vehicles_PlateNumber" ON vehicles ("PlateNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    ALTER TABLE transport_jobs ADD CONSTRAINT "FK_transport_jobs_vehicles_AssignedVehicleId" FOREIGN KEY ("AssignedVehicleId") REFERENCES vehicles ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031214543_AddJobStopsAndVehicles') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261031214543_AddJobStopsAndVehicles', '9.0.10');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031231031_AddInvoiceWithholdingAndIban') THEN
    ALTER TABLE invoices ADD "Iban" character varying(34);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031231031_AddInvoiceWithholdingAndIban') THEN
    ALTER TABLE invoices ADD "PayableAmount" numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031231031_AddInvoiceWithholdingAndIban') THEN
    ALTER TABLE invoices ADD "WithholdingAmount" numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031231031_AddInvoiceWithholdingAndIban') THEN
    ALTER TABLE invoices ADD "WithholdingCode" character varying(10);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031231031_AddInvoiceWithholdingAndIban') THEN
    ALTER TABLE invoices ADD "WithholdingRate" numeric(5,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031231031_AddInvoiceWithholdingAndIban') THEN
    UPDATE invoices SET "PayableAmount" = "GrandTotal";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031231031_AddInvoiceWithholdingAndIban') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261031231031_AddInvoiceWithholdingAndIban', '9.0.10');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102171101_AddJobContacts') THEN
    CREATE TABLE transport_job_contacts (
        "Id" uuid NOT NULL,
        "TransportJobId" uuid NOT NULL,
        "Role" character varying(50),
        "Name" character varying(100),
        "PhoneNumber" character varying(20) NOT NULL,
        "Sequence" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_transport_job_contacts" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_transport_job_contacts_sequence" CHECK ("Sequence" > 0),
        CONSTRAINT "FK_transport_job_contacts_transport_jobs_TransportJobId" FOREIGN KEY ("TransportJobId") REFERENCES transport_jobs ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102171101_AddJobContacts') THEN
    CREATE UNIQUE INDEX "IX_transport_job_contacts_TransportJobId_Sequence" ON transport_job_contacts ("TransportJobId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102171101_AddJobContacts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261102171101_AddJobContacts', '9.0.10');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102181431_AddVehicleTypes') THEN
    ALTER TABLE vehicles ALTER COLUMN "VehicleType" TYPE character varying(50);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102181431_AddVehicleTypes') THEN
    ALTER TABLE transport_jobs ALTER COLUMN "VehicleType" TYPE character varying(50);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102181431_AddVehicleTypes') THEN
    CREATE TABLE vehicle_types (
        "Id" uuid NOT NULL,
        "Code" character varying(50) NOT NULL,
        "Name" character varying(100) NOT NULL,
        "SortOrder" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        CONSTRAINT "PK_vehicle_types" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102181431_AddVehicleTypes') THEN
    INSERT INTO vehicle_types ("Id", "Code", "CreatedAt", "IsActive", "Name", "SortOrder", "UpdatedAt")
    VALUES ('0193a000-0000-7000-8000-000000000001', 'Panelvan', TIMESTAMPTZ '2026-11-02T00:00:00Z', TRUE, 'Panelvan', 10, NULL);
    INSERT INTO vehicle_types ("Id", "Code", "CreatedAt", "IsActive", "Name", "SortOrder", "UpdatedAt")
    VALUES ('0193a000-0000-7000-8000-000000000002', 'SmallPanelvan', TIMESTAMPTZ '2026-11-02T00:00:00Z', TRUE, 'Küçük Panelvan', 20, NULL);
    INSERT INTO vehicle_types ("Id", "Code", "CreatedAt", "IsActive", "Name", "SortOrder", "UpdatedAt")
    VALUES ('0193a000-0000-7000-8000-000000000003', 'Pickup', TIMESTAMPTZ '2026-11-02T00:00:00Z', TRUE, 'Kamyonet', 30, NULL);
    INSERT INTO vehicle_types ("Id", "Code", "CreatedAt", "IsActive", "Name", "SortOrder", "UpdatedAt")
    VALUES ('0193a000-0000-7000-8000-000000000004', 'Truck', TIMESTAMPTZ '2026-11-02T00:00:00Z', TRUE, 'Kamyon', 40, NULL);
    INSERT INTO vehicle_types ("Id", "Code", "CreatedAt", "IsActive", "Name", "SortOrder", "UpdatedAt")
    VALUES ('0193a000-0000-7000-8000-000000000005', 'Other', TIMESTAMPTZ '2026-11-02T00:00:00Z', TRUE, 'Diğer', 90, NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102181431_AddVehicleTypes') THEN
    CREATE UNIQUE INDEX "IX_vehicle_types_Code" ON vehicle_types ("Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102181431_AddVehicleTypes') THEN
    CREATE UNIQUE INDEX "IX_vehicle_types_Name" ON vehicle_types ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102181431_AddVehicleTypes') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261102181431_AddVehicleTypes', '9.0.10');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102183012_AddCustomerAdditionalEmails') THEN
    ALTER TABLE email_logs ALTER COLUMN "RecipientEmail" TYPE character varying(2200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102183012_AddCustomerAdditionalEmails') THEN
    ALTER TABLE customers ADD "AdditionalEmails" character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102183012_AddCustomerAdditionalEmails') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261102183012_AddCustomerAdditionalEmails', '9.0.10');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102184815_AddManualInvoiceEmails') THEN
    DROP INDEX "IX_email_logs_InvoiceId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102184815_AddManualInvoiceEmails') THEN
    CREATE UNIQUE INDEX "IX_email_logs_InvoiceId" ON email_logs ("InvoiceId") WHERE "Type" = 'InvoiceDelivery';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102184815_AddManualInvoiceEmails') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261102184815_AddManualInvoiceEmails', '9.0.10');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103062754_AddNotifications') THEN
    CREATE TABLE notifications (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Type" character varying(30) NOT NULL,
        "Title" character varying(150) NOT NULL,
        "Body" character varying(500) NOT NULL,
        "JobId" uuid,
        "ReadAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_notifications" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_notifications_transport_jobs_JobId" FOREIGN KEY ("JobId") REFERENCES transport_jobs ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_notifications_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103062754_AddNotifications') THEN
    CREATE TABLE push_devices (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Token" character varying(200) NOT NULL,
        "Platform" character varying(20),
        "CreatedAt" timestamp with time zone NOT NULL,
        "LastSeenAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_push_devices" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_push_devices_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103062754_AddNotifications') THEN
    CREATE INDEX "IX_notifications_JobId" ON notifications ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103062754_AddNotifications') THEN
    CREATE INDEX "IX_notifications_UserId_CreatedAt" ON notifications ("UserId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103062754_AddNotifications') THEN
    CREATE INDEX "IX_notifications_UserId_ReadAt" ON notifications ("UserId", "ReadAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103062754_AddNotifications') THEN
    CREATE UNIQUE INDEX "IX_push_devices_Token" ON push_devices ("Token");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103062754_AddNotifications') THEN
    CREATE INDEX "IX_push_devices_UserId" ON push_devices ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103062754_AddNotifications') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261103062754_AddNotifications', '9.0.10');
    END IF;
END $EF$;
COMMIT;

