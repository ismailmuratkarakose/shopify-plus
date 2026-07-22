using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Catalog.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogMasterOffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_ShopifyProductId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_Sku",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Categories_TenantId_Slug",
                schema: "catalog",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Price",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyProductId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Sku",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "catalog",
                table: "Categories");

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                schema: "catalog",
                table: "Products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Brand",
                schema: "catalog",
                table: "Products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                schema: "catalog",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Offers",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ShopifyProductId = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Offers_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Barcode",
                schema: "catalog",
                table: "Products",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                schema: "catalog",
                table: "Categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Offers_ProductId",
                schema: "catalog",
                table: "Offers",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_TenantId_ProductId",
                schema: "catalog",
                table: "Offers",
                columns: new[] { "TenantId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Offers_TenantId_ShopifyProductId",
                schema: "catalog",
                table: "Offers",
                columns: new[] { "TenantId", "ShopifyProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Offers",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "IX_Products_Barcode",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Slug",
                schema: "catalog",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "Barcode",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Brand",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                schema: "catalog",
                table: "Products");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "catalog",
                table: "Products",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "catalog",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncedAt",
                schema: "catalog",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                schema: "catalog",
                table: "Products",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "ShopifyProductId",
                schema: "catalog",
                table: "Products",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                schema: "catalog",
                table: "Products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "catalog",
                table: "Products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "catalog",
                table: "Products",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "catalog",
                table: "Categories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_ShopifyProductId",
                schema: "catalog",
                table: "Products",
                columns: new[] { "TenantId", "ShopifyProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Sku",
                schema: "catalog",
                table: "Products",
                columns: new[] { "TenantId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_TenantId_Slug",
                schema: "catalog",
                table: "Categories",
                columns: new[] { "TenantId", "Slug" },
                unique: true);
        }
    }
}
