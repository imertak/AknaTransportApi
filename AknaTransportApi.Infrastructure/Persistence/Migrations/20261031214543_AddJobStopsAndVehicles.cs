using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AknaTransportApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobStopsAndVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedVehicleId",
                table: "transport_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouteDescription",
                table: "transport_jobs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "transport_job_stops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    StopType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_job_stops", x => x.Id);
                    table.CheckConstraint("CK_transport_job_stops_sequence", "\"Sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_transport_job_stops_transport_jobs_TransportJobId",
                        column: x => x.TransportJobId,
                        principalTable: "transport_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VehicleType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "driver_vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnassignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_driver_vehicles_users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_driver_vehicles_users_DriverId",
                        column: x => x.DriverId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_driver_vehicles_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transport_jobs_AssignedVehicleId",
                table: "transport_jobs",
                column: "AssignedVehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_driver_vehicles_AssignedByUserId",
                table: "driver_vehicles",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_driver_vehicles_DriverId_IsActive",
                table: "driver_vehicles",
                columns: new[] { "DriverId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_driver_vehicles_VehicleId_active",
                table: "driver_vehicles",
                column: "VehicleId",
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_transport_job_stops_TransportJobId_StopType_Sequence",
                table: "transport_job_stops",
                columns: new[] { "TransportJobId", "StopType", "Sequence" },
                unique: true);

            // Mevcut işlerin tekli yükleme/teslimat adresleri durak kaydı olarak taşınır; hiçbir veri silinmez.
            // RouteDescription eski işlerde boş bırakılır: bu işlerin PDF ve fatura metni önceki açıklamayla aynı kalır.
            migrationBuilder.Sql("""
                INSERT INTO transport_job_stops ("Id", "TransportJobId", "StopType", "Address", "Latitude", "Longitude", "Sequence", "Description", "CreatedAt")
                SELECT gen_random_uuid(), j."Id", 'Pickup', j."PickupAddress", j."PickupLatitude", j."PickupLongitude", 1, NULL, j."CreatedAt"
                FROM transport_jobs j
                WHERE btrim(j."PickupAddress") <> '';

                INSERT INTO transport_job_stops ("Id", "TransportJobId", "StopType", "Address", "Latitude", "Longitude", "Sequence", "Description", "CreatedAt")
                SELECT gen_random_uuid(), j."Id", 'Delivery', j."DeliveryAddress", j."DeliveryLatitude", j."DeliveryLongitude", 1, NULL, j."CreatedAt"
                FROM transport_jobs j
                WHERE btrim(j."DeliveryAddress") <> '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_IsActive",
                table: "vehicles",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_PlateNumber",
                table: "vehicles",
                column: "PlateNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_transport_jobs_vehicles_AssignedVehicleId",
                table: "transport_jobs",
                column: "AssignedVehicleId",
                principalTable: "vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transport_jobs_vehicles_AssignedVehicleId",
                table: "transport_jobs");

            migrationBuilder.DropTable(
                name: "driver_vehicles");

            migrationBuilder.DropTable(
                name: "transport_job_stops");

            migrationBuilder.DropTable(
                name: "vehicles");

            migrationBuilder.DropIndex(
                name: "IX_transport_jobs_AssignedVehicleId",
                table: "transport_jobs");

            migrationBuilder.DropColumn(
                name: "AssignedVehicleId",
                table: "transport_jobs");

            migrationBuilder.DropColumn(
                name: "RouteDescription",
                table: "transport_jobs");
        }
    }
}
