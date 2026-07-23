using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Mobile.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class R1StoreScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "mobile",
                table: "RecentlyViewed",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_RecentlyViewed_TenantId_UserRef_ViewedAt",
                schema: "mobile",
                table: "RecentlyViewed",
                newName: "IX_RecentlyViewed_StoreId_UserRef_ViewedAt");

            migrationBuilder.RenameIndex(
                name: "IX_RecentlyViewed_TenantId_UserRef_ShopifyProductId",
                schema: "mobile",
                table: "RecentlyViewed",
                newName: "IX_RecentlyViewed_StoreId_UserRef_ShopifyProductId");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "mobile",
                table: "Favorites",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_Favorites_TenantId_UserRef_ShopifyProductId",
                schema: "mobile",
                table: "Favorites",
                newName: "IX_Favorites_StoreId_UserRef_ShopifyProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "mobile",
                table: "RecentlyViewed",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_RecentlyViewed_StoreId_UserRef_ViewedAt",
                schema: "mobile",
                table: "RecentlyViewed",
                newName: "IX_RecentlyViewed_TenantId_UserRef_ViewedAt");

            migrationBuilder.RenameIndex(
                name: "IX_RecentlyViewed_StoreId_UserRef_ShopifyProductId",
                schema: "mobile",
                table: "RecentlyViewed",
                newName: "IX_RecentlyViewed_TenantId_UserRef_ShopifyProductId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "mobile",
                table: "Favorites",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_Favorites_StoreId_UserRef_ShopifyProductId",
                schema: "mobile",
                table: "Favorites",
                newName: "IX_Favorites_TenantId_UserRef_ShopifyProductId");
        }
    }
}
