using System.Security.Claims;
using System.Text.Json;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Cms.Api.Components;
using Marketplace.Cms.Api.Domain;
using Marketplace.Cms.Api.Experience;
using Marketplace.Cms.Api.Infrastructure;
using Marketplace.Cms.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Cms.Api.Api;

// --- İstek sözleşmeleri ---
public record CreatePageRequest(string ScreenType, string Name, string Handle);
public record UpdatePageRequest(string? Name, bool? IsActive);
public record AddComponentRequest(string Type, JsonElement Settings, int? Position);
public record UpdateComponentRequest(JsonElement? Settings, bool? IsActive);
public record ReorderRequest(List<Guid> ComponentIds);
public record PublishRequest(string? Note);

// --- Yanıt sözleşmeleri ---
public record ComponentDto(Guid Id, string Type, int Position, JsonElement Settings, bool IsActive);
public record VersionDto(Guid Id, int VersionNumber, string Status, DateTimeOffset? PublishedAt,
    string? PublishedBy, string? Note, IReadOnlyList<ComponentDto> Components);
public record VersionSummaryDto(Guid Id, int VersionNumber, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt, string? PublishedBy, string? Note, int ComponentCount);
public record PageSummaryDto(Guid Id, string ScreenType, string Name, string Handle, bool IsActive,
    int? PublishedVersionNumber, bool HasDraft, DateTimeOffset? LastPublishedAt);
public record PageDetailDto(Guid Id, string ScreenType, string Name, string Handle, bool IsActive,
    int? PublishedVersionNumber, bool HasDraft, VersionDto? Draft, VersionDto? Published);

/// <summary>
/// Sayfa ve bileşen yönetimi (sürükle-bırak tasarımcının arka ucu) + Taslak→Yayın döngüsü.
/// Düzenlemeler her zaman TASLAK sürüm üzerinde yapılır; yayındaki içerik etkilenmez.
/// </summary>
public static class PageEndpoints
{
    public static IEndpointRouteBuilder MapPageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pages").RequireAuthorization().WithTags("Pages");

        // Tasarımcı paleti: desteklenen bileşen tipleri ve ayar şemaları.
        group.MapGet("/component-types", () => Results.Ok(ComponentTypes.All.Values.Select(d => new
        {
            type = d.Type,
            displayName = d.DisplayName,
            required = d.RequiredSettings,
            optional = d.OptionalSettings,
            description = d.Description
        })));

        // --- Sayfa CRUD ---
        group.MapPost("/", async (CreatePageRequest req, CmsDbContext db, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ScreenType>(req.ScreenType, true, out var screen))
                return Problem($"Geçersiz ekran tipi: '{req.ScreenType}'. Geçerli: {string.Join(", ", Enum.GetNames<ScreenType>())}",
                    StatusCodes.Status400BadRequest, "page.invalid_screen_type");
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Handle))
                return Problem("Ad ve kısa ad (handle) zorunludur.", StatusCodes.Status400BadRequest, "page.invalid");

            var handle = req.Handle.Trim().ToLowerInvariant();
            if (await db.Pages.AnyAsync(p => p.Handle == handle, ct))
                return Problem($"Bu kısa ad zaten kullanılıyor: '{handle}'", StatusCodes.Status409Conflict, "page.handle_exists");

            // Tekil ekranlar (ana sayfa, ürün listeleme, ürün detay, sepet) mağaza başına bir kez tanımlanır;
            // aksi hâlde mobil tarafta hangi sayfanın gösterileceği belirsiz kalır.
            if (ScreenTypes.IsSingleton(screen) && await db.Pages.AnyAsync(p => p.ScreenType == screen, ct))
                return Problem($"'{screen}' ekranı için bu mağazada zaten bir sayfa var. Mevcut sayfayı düzenleyin veya silin.",
                    StatusCodes.Status409Conflict, "page.screen_exists");

            var page = new Page { ScreenType = screen, Name = req.Name.Trim(), Handle = handle };
            page.Versions.Add(new PageVersion { VersionNumber = 1, Status = VersionStatus.Draft });
            db.Pages.Add(page);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/pages/{page.Id}", await BuildDetailAsync(db, page.Id, ct));
        });

        group.MapGet("/", async (CmsDbContext db, CancellationToken ct, string? screenType = null) =>
        {
            var q = db.Pages.Include(p => p.Versions).AsQueryable();
            if (!string.IsNullOrWhiteSpace(screenType) && Enum.TryParse<ScreenType>(screenType, true, out var st))
                q = q.Where(p => p.ScreenType == st);

            var pages = await q.OrderBy(p => p.Name).ToListAsync(ct);
            return Results.Ok(pages.Select(p =>
            {
                var published = p.Versions.FirstOrDefault(v => v.Id == p.PublishedVersionId);
                return new PageSummaryDto(p.Id, p.ScreenType.ToString(), p.Name, p.Handle, p.IsActive,
                    published?.VersionNumber, p.Versions.Any(v => v.Status == VersionStatus.Draft),
                    published?.PublishedAt);
            }));
        });

        group.MapGet("/{id:guid}", async (Guid id, CmsDbContext db, CancellationToken ct) =>
        {
            var dto = await BuildDetailAsync(db, id, ct);
            return dto is null ? PageNotFound(id) : Results.Ok(dto);
        });

        // Yayındaki içerik (mobil tarafın göreceği sürüm).
        group.MapGet("/{id:guid}/published", async (Guid id, CmsDbContext db, CancellationToken ct) =>
        {
            var page = await LoadPageAsync(db, id, ct);
            if (page is null) return PageNotFound(id);
            var published = page.Versions.FirstOrDefault(v => v.Id == page.PublishedVersionId);
            return published is null
                ? Problem("Bu sayfa henüz yayınlanmadı.", StatusCodes.Status404NotFound, "page.not_published")
                : Results.Ok(ToVersionDto(published));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdatePageRequest req, CmsDbContext db, CancellationToken ct) =>
        {
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return PageNotFound(id);
            if (!string.IsNullOrWhiteSpace(req.Name)) page.Name = req.Name.Trim();
            if (req.IsActive is { } active) page.IsActive = active;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildDetailAsync(db, id, ct));
        });

        group.MapDelete("/{id:guid}", async (Guid id, CmsDbContext db, CancellationToken ct) =>
        {
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return PageNotFound(id);
            db.Pages.Remove(page);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // --- Bileşen yerleşimi (her zaman TASLAK üzerinde) ---
        group.MapPost("/{id:guid}/components", async (Guid id, AddComponentRequest req, CmsDbContext db, CancellationToken ct) =>
        {
            var page = await LoadPageAsync(db, id, ct);
            if (page is null) return PageNotFound(id);

            var settingsJson = req.Settings.ValueKind == JsonValueKind.Undefined ? "{}" : req.Settings.GetRawText();
            if (!ComponentTypes.TryValidate(req.Type, settingsJson, out var error))
                return Problem(error, StatusCodes.Status400BadRequest, "component.invalid");

            var draft = await GetOrCreateDraftAsync(db, page, ct);
            var position = req.Position ?? (draft.Components.Count == 0 ? 0 : draft.Components.Max(c => c.Position) + 1);

            // Araya ekleme: sonraki bileşenleri kaydır.
            foreach (var c in draft.Components.Where(c => c.Position >= position))
                c.Position++;

            var component = new PageComponent
            {
                PageVersionId = draft.Id,
                Type = req.Type.ToLowerInvariant(),
                Position = position,
                SettingsJson = settingsJson,
                IsActive = true
            };
            draft.Components.Add(component);
            // Anahtar istemcide üretildiği için EF'in bunu "mevcut kayıt" sayıp UPDATE üretmemesi adına
            // yeni nesneyi DbSet'e açıkça ekliyoruz (Added durumu garanti).
            db.PageComponents.Add(component);

            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildDetailAsync(db, id, ct));
        });

        group.MapPut("/{id:guid}/components/{componentId:guid}", async (
            Guid id, Guid componentId, UpdateComponentRequest req, CmsDbContext db, CancellationToken ct) =>
        {
            var page = await LoadPageAsync(db, id, ct);
            if (page is null) return PageNotFound(id);

            var draft = await GetOrCreateDraftAsync(db, page, ct);
            var comp = draft.Components.FirstOrDefault(c => c.Id == componentId);
            if (comp is null)
                return Problem($"Bileşen taslakta bulunamadı: {componentId}", StatusCodes.Status404NotFound, "component.not_found");

            if (req.Settings is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } s)
            {
                var json = s.GetRawText();
                if (!ComponentTypes.TryValidate(comp.Type, json, out var error))
                    return Problem(error, StatusCodes.Status400BadRequest, "component.invalid");
                comp.SettingsJson = json;
            }
            if (req.IsActive is { } active) comp.IsActive = active;

            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildDetailAsync(db, id, ct));
        });

        group.MapDelete("/{id:guid}/components/{componentId:guid}", async (
            Guid id, Guid componentId, CmsDbContext db, CancellationToken ct) =>
        {
            var page = await LoadPageAsync(db, id, ct);
            if (page is null) return PageNotFound(id);

            var draft = await GetOrCreateDraftAsync(db, page, ct);
            var comp = draft.Components.FirstOrDefault(c => c.Id == componentId);
            if (comp is null)
                return Problem($"Bileşen taslakta bulunamadı: {componentId}", StatusCodes.Status404NotFound, "component.not_found");

            draft.Components.Remove(comp);
            db.PageComponents.Remove(comp);
            Renumber(draft);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildDetailAsync(db, id, ct));
        });

        // Sürükle-bırak sıralaması: bileşen kimlikleri istenen sırada gönderilir.
        group.MapPut("/{id:guid}/components/order", async (Guid id, ReorderRequest req, CmsDbContext db, CancellationToken ct) =>
        {
            var page = await LoadPageAsync(db, id, ct);
            if (page is null) return PageNotFound(id);

            var draft = await GetOrCreateDraftAsync(db, page, ct);
            var draftIds = draft.Components.Select(c => c.Id).ToHashSet();
            if (req.ComponentIds.Count != draftIds.Count || !req.ComponentIds.All(draftIds.Contains))
                return Problem("Sıralama listesi taslaktaki tüm bileşenleri (ve yalnızca onları) içermeli.",
                    StatusCodes.Status400BadRequest, "component.order_mismatch");

            for (var i = 0; i < req.ComponentIds.Count; i++)
                draft.Components.First(c => c.Id == req.ComponentIds[i]).Position = i;

            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildDetailAsync(db, id, ct));
        });

        // --- İçerik bütünlüğü ---
        // Bileşenlerin işaret ettiği ürün/koleksiyon/indirim hâlâ mağazada var mı?
        group.MapGet("/{id:guid}/validate", async (Guid id, CmsDbContext db, ContentValidator validator, CancellationToken ct) =>
        {
            var page = await LoadPageAsync(db, id, ct);
            if (page is null) return PageNotFound(id);

            var target = page.Versions.FirstOrDefault(v => v.Status == VersionStatus.Draft)
                         ?? page.Versions.FirstOrDefault(v => v.Id == page.PublishedVersionId);
            if (target is null)
                return Problem("Doğrulanacak içerik yok.", StatusCodes.Status404NotFound, "page.no_content");

            var issues = await validator.ValidateAsync(target, ct);
            return Results.Ok(new
            {
                versionNumber = target.VersionNumber,
                status = target.Status.ToString(),
                isValid = issues.All(i => i.Severity != ContentValidator.Error),
                errorCount = issues.Count(i => i.Severity == ContentValidator.Error),
                warningCount = issues.Count(i => i.Severity == ContentValidator.Warning),
                issues
            });
        });

        // --- Yayın döngüsü ---
        group.MapPost("/{id:guid}/publish", async (Guid id, PublishRequest? req, ClaimsPrincipal user,
            CmsDbContext db, ContentValidator validator, SnapshotBuilder snapshots, CancellationToken ct) =>
        {
            var page = await LoadPageAsync(db, id, ct);
            if (page is null) return PageNotFound(id);

            var draft = page.Versions.FirstOrDefault(v => v.Status == VersionStatus.Draft);
            if (draft is null)
                return Problem("Yayınlanacak taslak yok — sayfa zaten güncel.", StatusCodes.Status409Conflict, "page.no_draft");
            if (draft.Components.Count == 0)
                return Problem("Boş taslak yayınlanamaz; en az bir bileşen ekleyin.", StatusCodes.Status400BadRequest, "page.empty_draft");

            // Kırık referansla yayına çıkılmasın (mağaza verisine ulaşılamazsa yalnızca uyarı üretilir → engellemez).
            var issues = await validator.ValidateAsync(draft, ct);
            var errors = issues.Where(i => i.Severity == ContentValidator.Error).ToList();
            if (errors.Count > 0)
                return Results.Problem(
                    detail: $"İçerikte {errors.Count} kırık referans var; düzeltmeden yayınlanamaz.",
                    statusCode: StatusCodes.Status409Conflict,
                    title: "page.validation_failed",
                    extensions: new Dictionary<string, object?> { ["issues"] = errors });

            // Önceki yayın sürümü arşive alınır.
            foreach (var v in page.Versions.Where(v => v.Status == VersionStatus.Published))
                v.Status = VersionStatus.Archived;

            draft.Status = VersionStatus.Published;
            draft.PublishedAt = DateTimeOffset.UtcNow;
            draft.PublishedBy = user.FindFirstValue("preferred_username") ?? user.FindFirstValue("sub") ?? "bilinmiyor";
            draft.Note = req?.Note;
            page.PublishedVersionId = draft.Id;

            await db.SaveChangesAsync(ct);

            // Yayın, mobil uygulamanın okuduğu yapılandırmanın yeni bir sürümünü doğurur.
            var snapshot = await snapshots.RebuildAsync("publish", draft.PublishedBy, ct);

            var detail = await BuildDetailAsync(db, id, ct);
            return Results.Ok(new { page = detail, experienceVersion = snapshot.Version });
        });

        // --- Önizleme kanalı: yayınlanmamış taslağı test cihazında görmek için süreli anahtar ---
        group.MapPost("/{id:guid}/preview-token", async (Guid id, ClaimsPrincipal user, ITenantContext tenant,
            CmsDbContext db, IConfiguration config, CancellationToken ct, int? expiresInMinutes = null) =>
        {
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return PageNotFound(id);
            if (tenant.TenantId is not { } tenantId)
                return Problem("Mağaza kapsamı yok.", StatusCodes.Status401Unauthorized, "tenant.missing");

            var minutes = Math.Clamp(expiresInMinutes ?? config.GetValue<int?>("Preview:DefaultExpiryMinutes") ?? 60, 5, 1440);
            var token = new PreviewToken
            {
                TenantId = tenantId,
                PageId = page.Id,
                Token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(minutes),
                CreatedBy = user.FindFirstValue("preferred_username") ?? user.FindFirstValue("sub")
            };
            db.PreviewTokens.Add(token);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                token = token.Token,
                previewUrl = $"/api/preview/{token.Token}",
                expiresAt = token.ExpiresAt
            });
        });

        group.MapGet("/{id:guid}/versions", async (Guid id, CmsDbContext db, CancellationToken ct) =>
        {
            var page = await LoadPageAsync(db, id, ct);
            if (page is null) return PageNotFound(id);
            return Results.Ok(page.Versions
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new VersionSummaryDto(v.Id, v.VersionNumber, v.Status.ToString(), v.CreatedAt,
                    v.PublishedAt, v.PublishedBy, v.Note, v.Components.Count)));
        });

        // Geri alma: seçilen sürümün içeriği yeni bir taslağa kopyalanır (yayın etkilenmez).
        group.MapPost("/{id:guid}/versions/{versionId:guid}/restore", async (
            Guid id, Guid versionId, CmsDbContext db, CancellationToken ct) =>
        {
            var page = await LoadPageAsync(db, id, ct);
            if (page is null) return PageNotFound(id);

            var source = page.Versions.FirstOrDefault(v => v.Id == versionId);
            if (source is null)
                return Problem($"Sürüm bulunamadı: {versionId}", StatusCodes.Status404NotFound, "version.not_found");

            var draft = page.Versions.FirstOrDefault(v => v.Status == VersionStatus.Draft);
            if (draft is not null)
            {
                db.PageComponents.RemoveRange(draft.Components);
                draft.Components.Clear();
            }
            else
            {
                draft = new PageVersion
                {
                    PageId = page.Id,
                    VersionNumber = page.Versions.Max(v => v.VersionNumber) + 1,
                    Status = VersionStatus.Draft
                };
                page.Versions.Add(draft);
                db.PageVersions.Add(draft);
            }

            draft.Note = $"{source.VersionNumber}. sürümden geri yüklendi";
            CloneComponents(db, source, draft);

            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildDetailAsync(db, id, ct));
        });

        return app;
    }

    // --- Yardımcılar ---

    private static Task<Page?> LoadPageAsync(CmsDbContext db, Guid id, CancellationToken ct) =>
        db.Pages.Include(p => p.Versions).ThenInclude(v => v.Components)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>
    /// Düzenlenebilir taslağı döndürür. Taslak yoksa, yayındaki sürümün kopyasıyla yeni bir taslak açar
    /// (yayındaki içerik değişmeden düzenlemeye devam edilir).
    /// </summary>
    private static async Task<PageVersion> GetOrCreateDraftAsync(CmsDbContext db, Page page, CancellationToken ct)
    {
        var draft = page.Versions.FirstOrDefault(v => v.Status == VersionStatus.Draft);
        if (draft is not null) return draft;

        draft = new PageVersion
        {
            PageId = page.Id,
            VersionNumber = page.Versions.Count == 0 ? 1 : page.Versions.Max(v => v.VersionNumber) + 1,
            Status = VersionStatus.Draft
        };
        page.Versions.Add(draft);
        db.PageVersions.Add(draft);   // istemci tarafı anahtar → Added durumunu garantile

        var published = page.Versions.FirstOrDefault(v => v.Id == page.PublishedVersionId);
        if (published is not null)
            CloneComponents(db, published, draft);

        await db.SaveChangesAsync(ct);
        return draft;
    }

    /// <summary>Bir sürümün bileşenlerini hedef sürüme kopyalar (yeni kimliklerle).</summary>
    private static void CloneComponents(CmsDbContext db, PageVersion source, PageVersion target)
    {
        foreach (var c in source.Components.OrderBy(c => c.Position))
        {
            var copy = new PageComponent
            {
                PageVersionId = target.Id,
                Type = c.Type,
                Position = c.Position,
                SettingsJson = c.SettingsJson,
                IsActive = c.IsActive
            };
            target.Components.Add(copy);
            db.PageComponents.Add(copy);
        }
    }

    private static void Renumber(PageVersion version)
    {
        var i = 0;
        foreach (var c in version.Components.OrderBy(c => c.Position))
            c.Position = i++;
    }

    private static async Task<PageDetailDto?> BuildDetailAsync(CmsDbContext db, Guid id, CancellationToken ct)
    {
        var page = await LoadPageAsync(db, id, ct);
        if (page is null) return null;

        var draft = page.Versions.FirstOrDefault(v => v.Status == VersionStatus.Draft);
        var published = page.Versions.FirstOrDefault(v => v.Id == page.PublishedVersionId);

        return new PageDetailDto(page.Id, page.ScreenType.ToString(), page.Name, page.Handle, page.IsActive,
            published?.VersionNumber, draft is not null,
            draft is null ? null : ToVersionDto(draft),
            published is null ? null : ToVersionDto(published));
    }

    private static VersionDto ToVersionDto(PageVersion v) => new(
        v.Id, v.VersionNumber, v.Status.ToString(), v.PublishedAt, v.PublishedBy, v.Note,
        v.Components.OrderBy(c => c.Position)
            .Select(c => new ComponentDto(c.Id, c.Type, c.Position, ParseJson(c.SettingsJson), c.IsActive))
            .ToList());

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }

    private static IResult PageNotFound(Guid id) =>
        Problem($"Sayfa bulunamadı: {id}", StatusCodes.Status404NotFound, "page.not_found");

    private static IResult Problem(string detail, int status, string title) =>
        Results.Problem(detail, statusCode: status, title: title);
}
