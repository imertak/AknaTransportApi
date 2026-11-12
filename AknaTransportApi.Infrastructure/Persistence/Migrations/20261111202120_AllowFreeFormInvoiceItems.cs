using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AknaTransportApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowFreeFormInvoiceItems : Migration
    {
        /// <inheritdoc />
        /// <remarks>İşe bağlı olmayan (serbest) fatura kalemleri için invoice_items.JobId boş olabilir.</remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "invoice_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Serbest kalemlerin geri alınacak bir işi yoktur; geri dönüşte silinir.
            migrationBuilder.Sql("DELETE FROM invoice_items WHERE \"JobId\" IS NULL;");
            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "invoice_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
