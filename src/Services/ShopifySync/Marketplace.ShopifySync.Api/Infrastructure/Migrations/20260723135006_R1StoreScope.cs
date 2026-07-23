using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.ShopifySync.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class R1StoreScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "shopify",
                table: "SyncedProducts",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedProducts_TenantId_ShopifyProductId",
                schema: "shopify",
                table: "SyncedProducts",
                newName: "IX_SyncedProducts_StoreId_ShopifyProductId");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "shopify",
                table: "SyncedPages",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedPages_TenantId_ShopifyPageId",
                schema: "shopify",
                table: "SyncedPages",
                newName: "IX_SyncedPages_StoreId_ShopifyPageId");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "shopify",
                table: "SyncedOrders",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedOrders_TenantId_ShopifyOrderId",
                schema: "shopify",
                table: "SyncedOrders",
                newName: "IX_SyncedOrders_StoreId_ShopifyOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedOrders_TenantId_ShopifyCustomerId",
                schema: "shopify",
                table: "SyncedOrders",
                newName: "IX_SyncedOrders_StoreId_ShopifyCustomerId");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "shopify",
                table: "SyncedDiscounts",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedDiscounts_TenantId_ShopifyDiscountId",
                schema: "shopify",
                table: "SyncedDiscounts",
                newName: "IX_SyncedDiscounts_StoreId_ShopifyDiscountId");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "shopify",
                table: "SyncedCustomers",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedCustomers_TenantId_ShopifyCustomerId",
                schema: "shopify",
                table: "SyncedCustomers",
                newName: "IX_SyncedCustomers_StoreId_ShopifyCustomerId");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "shopify",
                table: "SyncedCollections",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedCollections_TenantId_ShopifyCollectionId",
                schema: "shopify",
                table: "SyncedCollections",
                newName: "IX_SyncedCollections_StoreId_ShopifyCollectionId");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "shopify",
                table: "SyncStates",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncStates_TenantId",
                schema: "shopify",
                table: "SyncStates",
                newName: "IX_SyncStates_StoreId");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "shopify",
                table: "ProductMappings",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductMappings_TenantId_Sku",
                schema: "shopify",
                table: "ProductMappings",
                newName: "IX_ProductMappings_StoreId_Sku");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "shopify",
                table: "Integrations",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_Integrations_TenantId",
                schema: "shopify",
                table: "Integrations",
                newName: "IX_Integrations_StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "shopify",
                table: "SyncedProducts",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedProducts_StoreId_ShopifyProductId",
                schema: "shopify",
                table: "SyncedProducts",
                newName: "IX_SyncedProducts_TenantId_ShopifyProductId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "shopify",
                table: "SyncedPages",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedPages_StoreId_ShopifyPageId",
                schema: "shopify",
                table: "SyncedPages",
                newName: "IX_SyncedPages_TenantId_ShopifyPageId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "shopify",
                table: "SyncedOrders",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedOrders_StoreId_ShopifyOrderId",
                schema: "shopify",
                table: "SyncedOrders",
                newName: "IX_SyncedOrders_TenantId_ShopifyOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedOrders_StoreId_ShopifyCustomerId",
                schema: "shopify",
                table: "SyncedOrders",
                newName: "IX_SyncedOrders_TenantId_ShopifyCustomerId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "shopify",
                table: "SyncedDiscounts",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedDiscounts_StoreId_ShopifyDiscountId",
                schema: "shopify",
                table: "SyncedDiscounts",
                newName: "IX_SyncedDiscounts_TenantId_ShopifyDiscountId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "shopify",
                table: "SyncedCustomers",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedCustomers_StoreId_ShopifyCustomerId",
                schema: "shopify",
                table: "SyncedCustomers",
                newName: "IX_SyncedCustomers_TenantId_ShopifyCustomerId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "shopify",
                table: "SyncedCollections",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncedCollections_StoreId_ShopifyCollectionId",
                schema: "shopify",
                table: "SyncedCollections",
                newName: "IX_SyncedCollections_TenantId_ShopifyCollectionId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "shopify",
                table: "SyncStates",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_SyncStates_StoreId",
                schema: "shopify",
                table: "SyncStates",
                newName: "IX_SyncStates_TenantId");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "shopify",
                table: "ProductMappings",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductMappings_StoreId_Sku",
                schema: "shopify",
                table: "ProductMappings",
                newName: "IX_ProductMappings_TenantId_Sku");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "shopify",
                table: "Integrations",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_Integrations_StoreId",
                schema: "shopify",
                table: "Integrations",
                newName: "IX_Integrations_TenantId");
        }
    }
}
