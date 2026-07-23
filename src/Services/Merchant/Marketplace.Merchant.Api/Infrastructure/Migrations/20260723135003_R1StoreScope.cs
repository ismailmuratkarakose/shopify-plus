using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Merchant.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class R1StoreScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "merchant",
                table: "Integrations",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_Integrations_TenantId_Provider",
                schema: "merchant",
                table: "Integrations",
                newName: "IX_Integrations_StoreId_Provider");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "merchant",
                table: "AuditEntries",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_AuditEntries_TenantId_OccurredAt",
                schema: "merchant",
                table: "AuditEntries",
                newName: "IX_AuditEntries_StoreId_OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "merchant",
                table: "Integrations",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_Integrations_StoreId_Provider",
                schema: "merchant",
                table: "Integrations",
                newName: "IX_Integrations_TenantId_Provider");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "merchant",
                table: "AuditEntries",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_AuditEntries_StoreId_OccurredAt",
                schema: "merchant",
                table: "AuditEntries",
                newName: "IX_AuditEntries_TenantId_OccurredAt");
        }
    }
}
