using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AknaTransportApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCancelAuditAndSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "transport_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "transport_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "transport_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "transport_jobs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "transport_jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_transport_jobs_CancelledByUserId",
                table: "transport_jobs",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_jobs_DeletedByUserId",
                table: "transport_jobs",
                column: "DeletedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_transport_jobs_users_CancelledByUserId",
                table: "transport_jobs",
                column: "CancelledByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transport_jobs_users_DeletedByUserId",
                table: "transport_jobs",
                column: "DeletedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Daha önce iptal edilmiş işlerde iptal eden kullanıcı durum geçmişindeki son iptal kaydından doldurulur.
            migrationBuilder.Sql("""
                UPDATE transport_jobs j
                SET "CancelledByUserId" = h."ChangedByUserId"
                FROM (
                    SELECT DISTINCT ON ("JobId") "JobId", "ChangedByUserId"
                    FROM job_status_histories
                    WHERE "ToStatus" = 'Cancelled'
                    ORDER BY "JobId", "ChangedAt" DESC
                ) h
                WHERE j."Id" = h."JobId" AND j."Status" = 'Cancelled' AND j."CancelledByUserId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transport_jobs_users_CancelledByUserId",
                table: "transport_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_transport_jobs_users_DeletedByUserId",
                table: "transport_jobs");

            migrationBuilder.DropIndex(
                name: "IX_transport_jobs_CancelledByUserId",
                table: "transport_jobs");

            migrationBuilder.DropIndex(
                name: "IX_transport_jobs_DeletedByUserId",
                table: "transport_jobs");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "transport_jobs");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "transport_jobs");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "transport_jobs");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "transport_jobs");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "transport_jobs");
        }
    }
}
