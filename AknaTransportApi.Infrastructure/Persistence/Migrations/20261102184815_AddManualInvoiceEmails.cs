using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AknaTransportApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualInvoiceEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_email_logs_InvoiceId",
                table: "email_logs");

            migrationBuilder.CreateIndex(
                name: "IX_email_logs_InvoiceId",
                table: "email_logs",
                column: "InvoiceId",
                unique: true,
                filter: "\"Type\" = 'InvoiceDelivery'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_email_logs_InvoiceId",
                table: "email_logs");

            migrationBuilder.CreateIndex(
                name: "IX_email_logs_InvoiceId",
                table: "email_logs",
                column: "InvoiceId",
                unique: true);
        }
    }
}
