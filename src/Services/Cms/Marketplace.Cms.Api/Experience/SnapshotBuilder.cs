using System.Text.Json.Nodes;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Cms.Api.Domain;
using Marketplace.Cms.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Cms.Api.Experience;

/// <summary>
/// Yayınlanan içerikten mobil uygulamanın tüketeceği DEĞİŞMEZ anlık görüntüyü üretir.
/// Her yayın (veya bayrak değişikliği) yeni bir sürüm doğurur; mobil taraf sürüm numarasıyla
/// önbellek geçerliliğini (ETag) yönetir.
/// </summary>
public sealed class SnapshotBuilder(CmsDbContext db, ITenantContext tenant, ILogger<SnapshotBuilder> logger)
{
    public async Task<ExperienceSnapshot> RebuildAsync(string reason, string? by, CancellationToken ct)
    {
        if (tenant.TenantId is not { } tenantId)
            throw new InvalidOperationException("Snapshot üretimi için mağaza kapsamı gerekli.");

        var pages = await db.Pages
            .Include(p => p.Versions).ThenInclude(v => v.Components)
            .Where(p => p.IsActive && p.PublishedVersionId != null)
            .ToListAsync(ct);

        var flags = await db.FeatureFlags.ToListAsync(ct);

        var version = (await db.ExperienceSnapshots.MaxAsync(s => (int?)s.Version, ct) ?? 0) + 1;

        var flagsNode = new JsonObject();
        foreach (var f in flags)
            flagsNode[f.Key] = new JsonObject { ["enabled"] = f.IsEnabled, ["value"] = f.Value };

        var pagesNode = new JsonArray();
        foreach (var p in pages.OrderBy(p => p.ScreenType).ThenBy(p => p.Handle))
        {
            var published = p.Versions.FirstOrDefault(v => v.Id == p.PublishedVersionId);
            if (published is null) continue;

            var components = new JsonArray();
            foreach (var c in published.Components.Where(c => c.IsActive).OrderBy(c => c.Position))
            {
                components.Add(new JsonObject
                {
                    ["id"] = c.Id.ToString(),
                    ["type"] = c.Type,
                    ["position"] = c.Position,
                    ["settings"] = JsonNode.Parse(string.IsNullOrWhiteSpace(c.SettingsJson) ? "{}" : c.SettingsJson)
                });
            }

            pagesNode.Add(new JsonObject
            {
                ["pageId"] = p.Id.ToString(),
                ["screenType"] = p.ScreenType.ToString(),
                ["handle"] = p.Handle,
                ["name"] = p.Name,
                ["versionNumber"] = published.VersionNumber,
                ["publishedAt"] = published.PublishedAt?.ToString("O"),
                ["components"] = components
            });
        }

        var root = new JsonObject
        {
            ["version"] = version,
            ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["reason"] = reason,
            ["flags"] = flagsNode,
            ["pages"] = pagesNode
        };

        var snapshot = new ExperienceSnapshot
        {
            TenantId = tenantId,
            Version = version,
            Json = root.ToJsonString(),
            GeneratedBy = by,
            Reason = reason
        };
        db.ExperienceSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Deneyim snapshot'ı üretildi: mağaza={Tenant} sürüm={Version} sayfa={Pages} sebep={Reason}",
            tenantId, version, pagesNode.Count, reason);
        return snapshot;
    }
}
