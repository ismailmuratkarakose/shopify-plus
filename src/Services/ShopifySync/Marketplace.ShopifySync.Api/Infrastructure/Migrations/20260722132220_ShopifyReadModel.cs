using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.ShopifySync.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShopifyReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncedCollections",
                schema: "shopify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopifyCollectionId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Handle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncedCollections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncedProducts",
                schema: "shopify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopifyProductId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Vendor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProductType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Handle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    ShopifyUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncedProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncedCollectionProduct",
                schema: "shopify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncedCollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopifyProductId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncedCollectionProduct", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncedCollectionProduct_SyncedCollections_SyncedCollectionId",
                        column: x => x.SyncedCollectionId,
                        principalSchema: "shopify",
                        principalTable: "SyncedCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncedVariant",
                schema: "shopify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncedProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopifyVariantId = table.Column<long>(type: "bigint", nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CompareAtPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    InventoryQuantity = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncedVariant", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncedVariant_SyncedProducts_SyncedProductId",
                        column: x => x.SyncedProductId,
                        principalSchema: "shopify",
                        principalTable: "SyncedProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncedCollectionProduct_SyncedCollectionId",
                schema: "shopify",
                table: "SyncedCollectionProduct",
                column: "SyncedCollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncedCollections_TenantId_ShopifyCollectionId",
                schema: "shopify",
                table: "SyncedCollections",
                columns: new[] { "TenantId", "ShopifyCollectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncedProducts_TenantId_ShopifyProductId",
                schema: "shopify",
                table: "SyncedProducts",
                columns: new[] { "TenantId", "ShopifyProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncedVariant_SyncedProductId",
                schema: "shopify",
                table: "SyncedVariant",
                column: "SyncedProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncedCollectionProduct",
                schema: "shopify");

            migrationBuilder.DropTable(
                name: "SyncedVariant",
                schema: "shopify");

            migrationBuilder.DropTable(
                name: "SyncedCollections",
                schema: "shopify");

            migrationBuilder.DropTable(
                name: "SyncedProducts",
                schema: "shopify");
        }
    }
}
