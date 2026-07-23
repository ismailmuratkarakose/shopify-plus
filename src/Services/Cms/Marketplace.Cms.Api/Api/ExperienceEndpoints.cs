using System.Security.Claims;
using System.Text.Json;
using Marketplace.BuildingBlocks.Auditing;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Cms.Api.Domain;
using Marketplace.Cms.Api.Experience;
using Marketplace.Cms.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Cms.Api.Api;

public record UpsertFlagRequest(bool IsEnabled, string? Value, string? Description);
public record FeatureFlagDto(string Key, bool IsEnabled, string? Value, string? Description, DateTimeOffset? UpdatedAt);

/// <summary>
/// Uzaktan yapılandırma: özellik bayrakları ve yayınlanan içeriğin sürümlü anlık görüntüsü.
/// Mobil uygulama snapshot'ı okur; sürüm numarası ETag olarak kullanılır.
/// </summary>
public static class ExperienceEndpoints
{
    public static IEndpointRouteBuilder MapExperienceEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Özellik bayrakları ---
        var flags = app.MapGroup("/api/flags").RequireAuthorization().WithTags("FeatureFlags");
        // Bayrak değişikliği canlı davranışı etkiler → yayın yetkisi gerekir.
        var flagWrite = app.MapGroup("/api/flags").RequireAuthorization(Policies.ContentPublish).WithTags("FeatureFlags");

        flags.MapGet("/", async (CmsDbContext db, CancellationToken ct) =>
        {
            var items = await db.FeatureFlags.OrderBy(f => f.Key).ToListAsync(ct);
            return Results.Ok(items.Select(f => new FeatureFlagDto(f.Key, f.IsEnabled, f.Value, f.Description, f.UpdatedAt)));
        });

        flagWrite.MapPut("/{key}", async (string key, UpsertFlagRequest req, ClaimsPrincipal user,
            CmsDbContext db, SnapshotBuilder snapshots, IAuditLogger audit, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(key))
                return Results.Problem("Bayrak anahtarı gerekli.", statusCode: StatusCodes.Status400BadRequest, title: "flag.invalid");

            var normalized = key.Trim();
            var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == normalized, ct);
            if (flag is null)
            {
                flag = new FeatureFlag { Key = normalized };
                db.FeatureFlags.Add(flag);
            }
            flag.IsEnabled = req.IsEnabled;
            flag.Value = req.Value;
            flag.Description = req.Description;
            audit.Record("flag.changed",
                $"'{flag.Key}' özellik bayrağı {(flag.IsEnabled ? "açıldı" : "kapatıldı")}",
                "FeatureFlag", flag.Key);
            await db.SaveChangesAsync(ct);

            // Bayrak değişikliği de yeni bir yapılandırma sürümü doğurur.
            var snap = await snapshots.RebuildAsync("flag_change",
                user.FindFirstValue("preferred_username") ?? user.FindFirstValue("sub"), ct);

            return Results.Ok(new
            {
                flag = new FeatureFlagDto(flag.Key, flag.IsEnabled, flag.Value, flag.Description, flag.UpdatedAt),
                experienceVersion = snap.Version
            });
        });

        flagWrite.MapDelete("/{key}", async (string key, ClaimsPrincipal user, CmsDbContext db,
            SnapshotBuilder snapshots, CancellationToken ct) =>
        {
            var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key, ct);
            if (flag is null) return Results.NotFound();
            db.FeatureFlags.Remove(flag);
            await db.SaveChangesAsync(ct);
            await snapshots.RebuildAsync("flag_change", user.FindFirstValue("sub"), ct);
            return Results.NoContent();
        });

        // --- Deneyim anlık görüntüsü ---
        var experience = app.MapGroup("/api/experience").RequireAuthorization().WithTags("Experience");
        var experienceWrite = app.MapGroup("/api/experience").RequireAuthorization(Policies.ContentPublish).WithTags("Experience");

        experience.MapGet("/current", async (HttpContext http, CmsDbContext db, CancellationToken ct) =>
        {
            var snap = await db.ExperienceSnapshots.OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
            if (snap is null)
                return Results.Problem("Henüz yayınlanmış içerik yok.", statusCode: StatusCodes.Status404NotFound,
                    title: "experience.not_published");

            // Sürüm numarası ETag olarak kullanılır: değişmediyse istemci 304 alır.
            var etag = $"\"v{snap.Version}\"";
            if (http.Request.Headers.IfNoneMatch.ToString() == etag)
                return Results.StatusCode(StatusCodes.Status304NotModified);

            http.Response.Headers.ETag = etag;
            return Results.Content(snap.Json, "application/json");
        });

        experience.MapGet("/versions", async (CmsDbContext db, CancellationToken ct) =>
        {
            var items = await db.ExperienceSnapshots.OrderByDescending(s => s.Version).Take(50)
                .Select(s => new { s.Version, s.CreatedAt, s.Reason, s.GeneratedBy })
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        experienceWrite.MapPost("/rebuild", async (ClaimsPrincipal user, SnapshotBuilder snapshots, CancellationToken ct) =>
        {
            var snap = await snapshots.RebuildAsync("manual",
                user.FindFirstValue("preferred_username") ?? user.FindFirstValue("sub"), ct);
            return Results.Ok(new { version = snap.Version, generatedAt = snap.CreatedAt });
        });

        return app;
    }
}
