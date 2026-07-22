using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.ShopifySync.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShopifyOrdersCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncedCustomers",
                schema: "shopify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopifyCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    FirstName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OrdersCount = table.Column<int>(type: "integer", nullable: false),
                    TotalSpent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShopifyCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ShopifyUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncedCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncedOrders",
                schema: "shopify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopifyOrderId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ShopifyCustomerId = table.Column<long>(type: "bigint", nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    FinancialStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FulfillmentStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TotalPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ShopifyCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ShopifyUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncedOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncedOrderLine",
                schema: "shopify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncedOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopifyLineId = table.Column<long>(type: "bigint", nullable: false),
                    ShopifyProductId = table.Column<long>(type: "bigint", nullable: false),
                    ShopifyVariantId = table.Column<long>(type: "bigint", nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncedOrderLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncedOrderLine_SyncedOrders_SyncedOrderId",
                        column: x => x.SyncedOrderId,
                        principalSchema: "shopify",
                        principalTable: "SyncedOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncedCustomers_TenantId_ShopifyCustomerId",
                schema: "shopify",
                table: "SyncedCustomers",
                columns: new[] { "TenantId", "ShopifyCustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncedOrderLine_SyncedOrderId",
                schema: "shopify",
                table: "SyncedOrderLine",
                column: "SyncedOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncedOrders_TenantId_ShopifyCustomerId",
                schema: "shopify",
                table: "SyncedOrders",
                columns: new[] { "TenantId", "ShopifyCustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncedOrders_TenantId_ShopifyOrderId",
                schema: "shopify",
                table: "SyncedOrders",
                columns: new[] { "TenantId", "ShopifyOrderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncedCustomers",
                schema: "shopify");

            migrationBuilder.DropTable(
                name: "SyncedOrderLine",
                schema: "shopify");

            migrationBuilder.DropTable(
                name: "SyncedOrders",
                schema: "shopify");
        }
    }
}
