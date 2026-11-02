using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AknaTransportApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "VehicleType",
                table: "vehicles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "VehicleType",
                table: "transport_jobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateTable(
                name: "vehicle_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_types", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "vehicle_types",
                columns: new[] { "Id", "Code", "CreatedAt", "IsActive", "Name", "SortOrder", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("0193a000-0000-7000-8000-000000000001"), "Panelvan", new DateTime(2026, 11, 2, 0, 0, 0, 0, DateTimeKind.Utc), true, "Panelvan", 10, null },
                    { new Guid("0193a000-0000-7000-8000-000000000002"), "SmallPanelvan", new DateTime(2026, 11, 2, 0, 0, 0, 0, DateTimeKind.Utc), true, "Küçük Panelvan", 20, null },
                    { new Guid("0193a000-0000-7000-8000-000000000003"), "Pickup", new DateTime(2026, 11, 2, 0, 0, 0, 0, DateTimeKind.Utc), true, "Kamyonet", 30, null },
                    { new Guid("0193a000-0000-7000-8000-000000000004"), "Truck", new DateTime(2026, 11, 2, 0, 0, 0, 0, DateTimeKind.Utc), true, "Kamyon", 40, null },
                    { new Guid("0193a000-0000-7000-8000-000000000005"), "Other", new DateTime(2026, 11, 2, 0, 0, 0, 0, DateTimeKind.Utc), true, "Diğer", 90, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_types_Code",
                table: "vehicle_types",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_types_Name",
                table: "vehicle_types",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicle_types");

            migrationBuilder.AlterColumn<string>(
                name: "VehicleType",
                table: "vehicles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "VehicleType",
                table: "transport_jobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);
        }
    }
}
