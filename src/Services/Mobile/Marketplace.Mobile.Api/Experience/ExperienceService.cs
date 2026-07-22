using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Marketplace.Mobile.Api.Clients;

namespace Marketplace.Mobile.Api.Experience;

public sealed class CachedSnapshot
{
    public required JsonNode Root { get; init; }
    public required string? ETag { get; init; }
    public required int Version { get; init; }
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>
/// Mağaza başına deneyim anlık görüntüsü önbelleği. Mobil trafiği her istekte CMS'e gitmez;
/// TTL dolduğunda ETag ile yeniden doğrulama yapılır (değişmemişse 304 → gövde indirilmez).
/// </summary>
public sealed class ExperienceCache
{
    private readonly ConcurrentDictionary<Guid, CachedSnapshot> _entries = new();

    public bool TryGet(Guid tenantId, out CachedSnapshot? entry) => _entries.TryGetValue(tenantId, out entry);
    public void Set(Guid tenantId, CachedSnapshot entry) => _entries[tenantId] = entry;
    public void Invalidate(Guid tenantId) => _entries.TryRemove(tenantId, out _);
}

/// <summary>Snapshot'ı önbellekten veya CMS'ten getirir ve ekran/bayrak sorgularını yanıtlar.</summary>
public sealed class ExperienceService(
    IExperienceClient client,
    ExperienceCache cache,
    IConfiguration config,
    ILogger<ExperienceService> logger)
{
    private TimeSpan Ttl => TimeSpan.FromSeconds(config.GetValue<int?>("Experience:CacheTtlSeconds") ?? 30);

    public async Task<CachedSnapshot?> GetAsync(Guid tenantId, CancellationToken ct)
    {
        cache.TryGet(tenantId, out var entry);
        var fresh = entry is not null && DateTimeOffset.UtcNow - entry.FetchedAt < Ttl;
        if (fresh) return entry;

        var res = await client.GetCurrentAsync(entry?.ETag, ct);

        // CMS'e ulaşılamadıysa elimizdeki (bayat da olsa) sürümle devam et — mobil ekran boş kalmasın.
        if (res is null)
        {
            if (entry is not null)
            {
                logger.LogWarning("Deneyim güncellenemedi; önbellekteki sürüm kullanılıyor (mağaza {Tenant}).", tenantId);
                entry.FetchedAt = DateTimeOffset.UtcNow;
                return entry;
            }
            return null;
        }

        if (res.NotModified && entry is not null)
        {
            entry.FetchedAt = DateTimeOffset.UtcNow;
            return entry;
        }

        if (string.IsNullOrEmpty(res.Json)) return entry;

        var root = JsonNode.Parse(res.Json)!;
        var version = root["version"]?.GetValue<int>() ?? 0;
        var updated = new CachedSnapshot
        {
            Root = root,
            ETag = res.ETag,
            Version = version,
            FetchedAt = DateTimeOffset.UtcNow
        };
        cache.Set(tenantId, updated);
        return updated;
    }

    /// <summary>Ekranı önce ScreenType, bulunamazsa handle ile çözer.</summary>
    public static JsonNode? ResolveScreen(JsonNode snapshot, string screen)
    {
        if (snapshot["pages"] is not JsonArray pages) return null;

        foreach (var p in pages)
        {
            if (string.Equals(p?["screenType"]?.GetValue<string>(), screen, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        foreach (var p in pages)
        {
            if (string.Equals(p?["handle"]?.GetValue<string>(), screen, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }
}
