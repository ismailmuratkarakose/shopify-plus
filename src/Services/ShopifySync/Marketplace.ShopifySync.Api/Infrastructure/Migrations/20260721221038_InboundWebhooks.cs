using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.ShopifySync.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InboundWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductMappings_TenantId_MarketplaceProductId",
                schema: "shopify",
                table: "ProductMappings");

            migrationBuilder.CreateTable(
                name: "WebhookInbox",
                schema: "shopify",
                columns: table => new
                {
                    WebhookId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookInbox", x => x.WebhookId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductMappings_TenantId_Sku",
                schema: "shopify",
                table: "ProductMappings",
                columns: new[] { "TenantId", "Sku" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookInbox",
                schema: "shopify");

            migrationBuilder.DropIndex(
                name: "IX_ProductMappings_TenantId_Sku",
                schema: "shopify",
                table: "ProductMappings");

            migrationBuilder.CreateIndex(
                name: "IX_ProductMappings_TenantId_MarketplaceProductId",
                schema: "shopify",
                table: "ProductMappings",
                columns: new[] { "TenantId", "MarketplaceProductId" },
                unique: true);
        }
    }
}
