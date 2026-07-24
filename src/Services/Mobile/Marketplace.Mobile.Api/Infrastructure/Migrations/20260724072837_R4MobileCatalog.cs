using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Mobile.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class R4MobileCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecentlyViewed_StoreId_UserRef_ShopifyProductId",
                schema: "mobile",
                table: "RecentlyViewed");

            migrationBuilder.DropIndex(
                name: "IX_RecentlyViewed_StoreId_UserRef_ViewedAt",
                schema: "mobile",
                table: "RecentlyViewed");

            migrationBuilder.DropIndex(
                name: "IX_Favorites_StoreId_UserRef_ShopifyProductId",
                schema: "mobile",
                table: "Favorites");

            migrationBuilder.DropColumn(
                name: "ShopifyProductId",
                schema: "mobile",
                table: "RecentlyViewed");

            migrationBuilder.DropColumn(
                name: "ShopifyProductId",
                schema: "mobile",
                table: "Favorites");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "mobile",
                table: "RecentlyViewed",
                newName: "ProductId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "mobile",
                table: "Favorites",
                newName: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyViewed_UserRef_ProductId",
                schema: "mobile",
                table: "RecentlyViewed",
                columns: new[] { "UserRef", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyViewed_UserRef_ViewedAt",
                schema: "mobile",
                table: "RecentlyViewed",
                columns: new[] { "UserRef", "ViewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserRef_ProductId",
                schema: "mobile",
                table: "Favorites",
                columns: new[] { "UserRef", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecentlyViewed_UserRef_ProductId",
                schema: "mobile",
                table: "RecentlyViewed");

            migrationBuilder.DropIndex(
                name: "IX_RecentlyViewed_UserRef_ViewedAt",
                schema: "mobile",
                table: "RecentlyViewed");

            migrationBuilder.DropIndex(
                name: "IX_Favorites_UserRef_ProductId",
                schema: "mobile",
                table: "Favorites");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                schema: "mobile",
                table: "RecentlyViewed",
                newName: "StoreId");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                schema: "mobile",
                table: "Favorites",
                newName: "StoreId");

            migrationBuilder.AddColumn<long>(
                name: "ShopifyProductId",
                schema: "mobile",
                table: "RecentlyViewed",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ShopifyProductId",
                schema: "mobile",
                table: "Favorites",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyViewed_StoreId_UserRef_ShopifyProductId",
                schema: "mobile",
                table: "RecentlyViewed",
                columns: new[] { "StoreId", "UserRef", "ShopifyProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyViewed_StoreId_UserRef_ViewedAt",
                schema: "mobile",
                table: "RecentlyViewed",
                columns: new[] { "StoreId", "UserRef", "ViewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_StoreId_UserRef_ShopifyProductId",
                schema: "mobile",
                table: "Favorites",
                columns: new[] { "StoreId", "UserRef", "ShopifyProductId" },
                unique: true);
        }
    }
}
