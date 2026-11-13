using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AknaTransportApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransportOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "offer_number_seq");

            migrationBuilder.CreateTable(
                name: "transport_offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OfferDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactPerson = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CustomerPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CustomerEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LoadingPoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LoadingAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnloadingPoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnloadingAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TransportType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VehicleType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CargoDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LoadingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DistanceKm = table.Column<decimal>(type: "numeric(10,1)", precision: 10, scale: 1, nullable: true),
                    SubTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaymentTerms = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DeliveryTerms = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CustomerNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InternalNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConvertedJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConvertedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CopiedFromOfferId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_offers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_offers_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_offers_transport_jobs_ConvertedJobId",
                        column: x => x.ConvertedJobId,
                        principalTable: "transport_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_offers_transport_offers_CopiedFromOfferId",
                        column: x => x.CopiedFromOfferId,
                        principalTable: "transport_offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_transport_offers_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_offers_users_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transport_offer_histories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_offer_histories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_offer_histories_transport_offers_TransportOfferId",
                        column: x => x.TransportOfferId,
                        principalTable: "transport_offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transport_offer_histories_users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transport_offer_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_offer_items", x => x.Id);
                    table.CheckConstraint("CK_transport_offer_items_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_transport_offer_items_transport_offers_TransportOfferId",
                        column: x => x.TransportOfferId,
                        principalTable: "transport_offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transport_offer_histories_ChangedByUserId",
                table: "transport_offer_histories",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_offer_histories_TransportOfferId_CreatedAt",
                table: "transport_offer_histories",
                columns: new[] { "TransportOfferId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_offer_items_TransportOfferId_Sequence",
                table: "transport_offer_items",
                columns: new[] { "TransportOfferId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_offers_ConvertedJobId",
                table: "transport_offers",
                column: "ConvertedJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_offers_CopiedFromOfferId",
                table: "transport_offers",
                column: "CopiedFromOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_offers_CreatedByUserId",
                table: "transport_offers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_offers_CustomerId_OfferDate",
                table: "transport_offers",
                columns: new[] { "CustomerId", "OfferDate" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_offers_DeletedByUserId",
                table: "transport_offers",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_offers_OfferDate",
                table: "transport_offers",
                column: "OfferDate");

            migrationBuilder.CreateIndex(
                name: "IX_transport_offers_OfferNumber",
                table: "transport_offers",
                column: "OfferNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_offers_Status_ValidUntil",
                table: "transport_offers",
                columns: new[] { "Status", "ValidUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transport_offer_histories");

            migrationBuilder.DropTable(
                name: "transport_offer_items");

            migrationBuilder.DropTable(
                name: "transport_offers");

            migrationBuilder.DropSequence(
                name: "offer_number_seq");
        }
    }
}
