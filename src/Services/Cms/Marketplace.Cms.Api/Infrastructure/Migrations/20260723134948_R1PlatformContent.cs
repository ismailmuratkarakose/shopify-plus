using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Cms.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class R1PlatformContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pages_TenantId_Handle",
                schema: "cms",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_Pages_TenantId_ScreenType",
                schema: "cms",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_TenantId_CreatedAt",
                schema: "cms",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_FeatureFlags_TenantId_Key",
                schema: "cms",
                table: "FeatureFlags");

            migrationBuilder.DropIndex(
                name: "IX_ExperienceSnapshots_TenantId_Version",
                schema: "cms",
                table: "ExperienceSnapshots");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "cms",
                table: "PreviewTokens");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "cms",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "cms",
                table: "PageVersions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "cms",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "cms",
                table: "FeatureFlags");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "cms",
                table: "ExperienceSnapshots");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "cms",
                table: "AuditEntries",
                newName: "StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_AuditEntries_TenantId_OccurredAt",
                schema: "cms",
                table: "AuditEntries",
                newName: "IX_AuditEntries_StoreId_OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_Handle",
                schema: "cms",
                table: "Pages",
                column: "Handle",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pages_ScreenType",
                schema: "cms",
                table: "Pages",
                column: "ScreenType");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_CreatedAt",
                schema: "cms",
                table: "MediaAssets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_Key",
                schema: "cms",
                table: "FeatureFlags",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceSnapshots_Version",
                schema: "cms",
                table: "ExperienceSnapshots",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pages_Handle",
                schema: "cms",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_Pages_ScreenType",
                schema: "cms",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_CreatedAt",
                schema: "cms",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_FeatureFlags_Key",
                schema: "cms",
                table: "FeatureFlags");

            migrationBuilder.DropIndex(
                name: "IX_ExperienceSnapshots_Version",
                schema: "cms",
                table: "ExperienceSnapshots");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                schema: "cms",
                table: "AuditEntries",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_AuditEntries_StoreId_OccurredAt",
                schema: "cms",
                table: "AuditEntries",
                newName: "IX_AuditEntries_TenantId_OccurredAt");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "cms",
                table: "PreviewTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "cms",
                table: "Pages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "cms",
                table: "PageVersions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "cms",
                table: "MediaAssets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "cms",
                table: "FeatureFlags",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "cms",
                table: "ExperienceSnapshots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Pages_TenantId_Handle",
                schema: "cms",
                table: "Pages",
                columns: new[] { "TenantId", "Handle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pages_TenantId_ScreenType",
                schema: "cms",
                table: "Pages",
                columns: new[] { "TenantId", "ScreenType" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_TenantId_CreatedAt",
                schema: "cms",
                table: "MediaAssets",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_TenantId_Key",
                schema: "cms",
                table: "FeatureFlags",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceSnapshots_TenantId_Version",
                schema: "cms",
                table: "ExperienceSnapshots",
                columns: new[] { "TenantId", "Version" },
                unique: true);
        }
    }
}
