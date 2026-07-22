using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Mobile.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mobile");

            migrationBuilder.CreateTable(
                name: "Favorites",
                schema: "mobile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShopifyProductId = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecentlyViewed",
                schema: "mobile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShopifyProductId = table.Column<long>(type: "bigint", nullable: false),
                    ViewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecentlyViewed", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_TenantId_UserRef_ShopifyProductId",
                schema: "mobile",
                table: "Favorites",
                columns: new[] { "TenantId", "UserRef", "ShopifyProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyViewed_TenantId_UserRef_ShopifyProductId",
                schema: "mobile",
                table: "RecentlyViewed",
                columns: new[] { "TenantId", "UserRef", "ShopifyProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyViewed_TenantId_UserRef_ViewedAt",
                schema: "mobile",
                table: "RecentlyViewed",
                columns: new[] { "TenantId", "UserRef", "ViewedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Favorites",
                schema: "mobile");

            migrationBuilder.DropTable(
                name: "RecentlyViewed",
                schema: "mobile");
        }
    }
}
