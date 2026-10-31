using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AknaTransportApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceWithholdingAndIban : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "invoices",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PayableAmount",
                table: "invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingAmount",
                table: "invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WithholdingCode",
                table: "invoices",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingRate",
                table: "invoices",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Mevcut faturalarda tevkifat yoktur: ödenecek tutar genel toplamdır.
            migrationBuilder.Sql("UPDATE invoices SET \"PayableAmount\" = \"GrandTotal\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Iban",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "PayableAmount",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "WithholdingAmount",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "WithholdingCode",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "WithholdingRate",
                table: "invoices");
        }
    }
}
