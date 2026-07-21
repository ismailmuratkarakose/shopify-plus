using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.ShopifySync.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialShopifySync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shopify");

            migrationBuilder.CreateTable(
                name: "Integrations",
                schema: "shopify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopDomain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Integrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "shopify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductMappings",
                schema: "shopify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopifyProductId = table.Column<long>(type: "bigint", nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_TenantId",
                schema: "shopify",
                table: "Integrations",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedOn_OccurredOn",
                schema: "shopify",
                table: "OutboxMessages",
                columns: new[] { "ProcessedOn", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductMappings_TenantId_MarketplaceProductId",
                schema: "shopify",
                table: "ProductMappings",
                columns: new[] { "TenantId", "MarketplaceProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Integrations",
                schema: "shopify");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "shopify");

            migrationBuilder.DropTable(
                name: "ProductMappings",
                schema: "shopify");
        }
    }
}
