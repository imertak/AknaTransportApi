using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AknaTransportApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transport_job_contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_job_contacts", x => x.Id);
                    table.CheckConstraint("CK_transport_job_contacts_sequence", "\"Sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_transport_job_contacts_transport_jobs_TransportJobId",
                        column: x => x.TransportJobId,
                        principalTable: "transport_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transport_job_contacts_TransportJobId_Sequence",
                table: "transport_job_contacts",
                columns: new[] { "TransportJobId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transport_job_contacts");
        }
    }
}
