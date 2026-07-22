using System.Security.Claims;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Cms.Api.Domain;
using Marketplace.Cms.Api.Infrastructure;
using Marketplace.Cms.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Cms.Api.Api;

public record MediaAssetDto(Guid Id, string FileName, string ContentType, long SizeBytes,
    string Url, DateTimeOffset CreatedAt, string? UploadedBy);

/// <summary>
/// Banner/kampanya görsellerinin yüklendiği medya servisi. Dosya içeriği anonim servis edilir
/// (mobil uygulama ve CDN erişebilsin); yükleme/silme/listeleme yetki ister.
/// </summary>
public static class MediaEndpoints
{
    private static readonly string[] AllowedTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "image/svg+xml"];

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/media").WithTags("Media");

        group.MapPost("/", async (HttpRequest request, ClaimsPrincipal user, ITenantContext tenant,
            IMediaStorage storage, CmsDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            if (tenant.TenantId is not { } tenantId)
                return Results.Problem("Mağaza kapsamı yok.", statusCode: StatusCodes.Status401Unauthorized, title: "tenant.missing");
            if (!request.HasFormContentType)
                return Results.Problem("Dosya multipart/form-data olarak gönderilmeli.",
                    statusCode: StatusCodes.Status400BadRequest, title: "media.invalid_request");

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.Problem("Dosya bulunamadı.", statusCode: StatusCodes.Status400BadRequest, title: "media.no_file");

            var maxBytes = config.GetValue<long?>("Media:MaxSizeBytes") ?? 5 * 1024 * 1024;
            if (file.Length > maxBytes)
                return Results.Problem($"Dosya çok büyük ({file.Length} bayt). Üst sınır: {maxBytes} bayt.",
                    statusCode: StatusCodes.Status400BadRequest, title: "media.too_large");

            if (!AllowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
                return Results.Problem($"Desteklenmeyen dosya tipi: {file.ContentType}. İzinliler: {string.Join(", ", AllowedTypes)}",
                    statusCode: StatusCodes.Status400BadRequest, title: "media.unsupported_type");

            await using var stream = file.OpenReadStream();
            var path = await storage.SaveAsync(stream, tenantId, file.FileName, ct);

            var asset = new MediaAsset
            {
                TenantId = tenantId,
                FileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                StoragePath = path,
                UploadedBy = user.FindFirstValue("preferred_username") ?? user.FindFirstValue("sub")
            };
            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/media/{asset.Id}", ToDto(asset));
        }).RequireAuthorization(Policies.ContentEdit).DisableAntiforgery();

        group.MapGet("/", async (CmsDbContext db, CancellationToken ct) =>
        {
            var items = await db.MediaAssets.OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync(ct);
            return Results.Ok(items.Select(ToDto));
        }).RequireAuthorization();

        // Dosya içeriği: anonim (görseller gizli değil; kimlik tahmin edilemez GUID).
        group.MapGet("/{id:guid}/content", async (Guid id, CmsDbContext db, IMediaStorage storage, CancellationToken ct) =>
        {
            var asset = await db.MediaAssets.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id, ct);
            if (asset is null) return Results.NotFound();

            var stream = await storage.OpenAsync(asset.StoragePath, ct);
            if (stream is null) return Results.NotFound();

            return Results.File(stream, asset.ContentType, asset.FileName);
        }).AllowAnonymous();

        group.MapDelete("/{id:guid}", async (Guid id, CmsDbContext db, IMediaStorage storage, CancellationToken ct) =>
        {
            var asset = await db.MediaAssets.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (asset is null) return Results.NotFound();

            await storage.DeleteAsync(asset.StoragePath, ct);
            db.MediaAssets.Remove(asset);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.ContentEdit);

        return app;
    }

    private static MediaAssetDto ToDto(MediaAsset a) =>
        new(a.Id, a.FileName, a.ContentType, a.SizeBytes, $"/api/media/{a.Id}/content", a.CreatedAt, a.UploadedBy);
}
