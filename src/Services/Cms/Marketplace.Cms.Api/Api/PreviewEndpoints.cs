using Marketplace.Cms.Api.Domain;
using Marketplace.Cms.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Cms.Api.Api;

/// <summary>
/// Önizleme kanalı: yayınlanmamış taslağı, yönetim paneline giriş yapmamış bir test cihazında göstermek için.
/// Anonimdir — kiracıyı ve sayfayı anahtarın kendisi belirler; anahtar süreli ve iptal edilebilir.
/// </summary>
public static class PreviewEndpoints
{
    public static IEndpointRouteBuilder MapPreviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/preview/{token}", async (string token, CmsDbContext db, CancellationToken ct) =>
        {
            // Anahtar tabanlı erişim → kiracı filtresi atlanır (kapsam anahtarla belirlenir).
            var pt = await db.PreviewTokens
                .FirstOrDefaultAsync(t => t.Token == token, ct);

            if (pt is null)
                return Results.Problem("Önizleme anahtarı geçersiz.", statusCode: StatusCodes.Status404NotFound, title: "preview.invalid");
            if (!pt.IsValid(DateTimeOffset.UtcNow))
                return Results.Problem("Önizleme anahtarının süresi dolmuş veya iptal edilmiş.",
                    statusCode: StatusCodes.Status410Gone, title: "preview.expired");

            var page = await db.Pages
                .Include(p => p.Versions).ThenInclude(v => v.Components)
                .FirstOrDefaultAsync(p => p.Id == pt.PageId, ct);
            if (page is null)
                return Results.Problem("Sayfa bulunamadı.", statusCode: StatusCodes.Status404NotFound, title: "page.not_found");

            // Önizlemede taslak öncelikli; taslak yoksa yayındaki içerik gösterilir.
            var version = page.Versions.FirstOrDefault(v => v.Status == VersionStatus.Draft)
                          ?? page.Versions.FirstOrDefault(v => v.Id == page.PublishedVersionId);
            if (version is null)
                return Results.Problem("Gösterilecek içerik yok.", statusCode: StatusCodes.Status404NotFound, title: "page.no_content");

            return Results.Ok(new
            {
                pageId = page.Id,
                screenType = page.ScreenType.ToString(),
                name = page.Name,
                handle = page.Handle,
                isPreview = version.Status == VersionStatus.Draft,
                version = new
                {
                    version.VersionNumber,
                    status = version.Status.ToString(),
                    components = version.Components.OrderBy(c => c.Position).Select(c => new
                    {
                        c.Id,
                        c.Type,
                        c.Position,
                        c.IsActive,
                        settings = System.Text.Json.JsonDocument.Parse(
                            string.IsNullOrWhiteSpace(c.SettingsJson) ? "{}" : c.SettingsJson).RootElement.Clone()
                    })
                },
                expiresAt = pt.ExpiresAt
            });
        })
        .AllowAnonymous()
        .WithTags("Preview");

        return app;
    }
}
