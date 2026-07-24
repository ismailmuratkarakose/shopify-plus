using Marketplace.BuildingBlocks.Auditing;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Catalog.Api.Domain;
using Marketplace.Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Api;

public record CategoryDto(Guid Id, string Name, string Slug, Guid? ParentId, int SortOrder, bool IsActive);
public record UpsertCategoryRequest(string Name, Guid? ParentId, int SortOrder = 0, bool IsActive = true);

/// <summary>
/// Pazaryeri kategori ağacı. Taksonomiyi platform personeli yönetir; okuma kamusaldır
/// (mobil uygulama ve mağaza panelleri aynı ağacı görür).
/// </summary>
public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/api/catalog/categories").WithTags("Categories").AllowAnonymous();
        var write = app.MapGroup("/api/catalog/categories").WithTags("Categories")
            .RequireAuthorization(Policies.Owner);

        read.MapGet("/", async (CatalogDbContext db, CancellationToken ct, bool includeInactive = false) =>
        {
            var q = db.Categories.AsQueryable();
            if (!includeInactive) q = q.Where(c => c.IsActive);
            var items = await q.OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.ParentId, c.SortOrder, c.IsActive))
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        write.MapPost("/", async (UpsertCategoryRequest req, CatalogDbContext db, IAuditLogger audit,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.Problem("Kategori adı gerekli.", statusCode: StatusCodes.Status400BadRequest,
                    title: "category.name_required");

            if (req.ParentId is { } pid && !await db.Categories.AnyAsync(c => c.Id == pid, ct))
                return Results.Problem("Üst kategori bulunamadı.", statusCode: StatusCodes.Status400BadRequest,
                    title: "category.parent_not_found");

            var slug = Slugify(req.Name);
            if (await db.Categories.AnyAsync(c => c.Slug == slug, ct))
                return Results.Problem($"'{slug}' slug'ı zaten mevcut.", statusCode: StatusCodes.Status409Conflict,
                    title: "category.slug_exists");

            var category = new Category
            {
                Name = req.Name.Trim(),
                Slug = slug,
                ParentId = req.ParentId,
                SortOrder = req.SortOrder,
                IsActive = req.IsActive
            };
            db.Categories.Add(category);
            audit.Record("category.create", $"'{category.Name}' kategorisi oluşturuldu", "Category", category.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/catalog/categories/{category.Id}",
                new CategoryDto(category.Id, category.Name, category.Slug, category.ParentId, category.SortOrder, category.IsActive));
        });

        write.MapPut("/{id:guid}", async (Guid id, UpsertCategoryRequest req, CatalogDbContext db,
            IAuditLogger audit, CancellationToken ct) =>
        {
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (category is null) return Results.NotFound();

            if (req.ParentId == id)
                return Results.Problem("Kategori kendi üst kategorisi olamaz.",
                    statusCode: StatusCodes.Status400BadRequest, title: "category.self_parent");

            category.Name = req.Name.Trim();
            category.ParentId = req.ParentId;
            category.SortOrder = req.SortOrder;
            category.IsActive = req.IsActive;
            audit.Record("category.update", $"'{category.Name}' kategorisi güncellendi", "Category", id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.Ok(new CategoryDto(category.Id, category.Name, category.Slug, category.ParentId,
                category.SortOrder, category.IsActive));
        });

        write.MapDelete("/{id:guid}", async (Guid id, CatalogDbContext db, IAuditLogger audit, CancellationToken ct) =>
        {
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (category is null) return Results.NotFound();

            // Ürün bağlıysa veya alt kategorisi varsa silme — pasifleştir (kart referansları kırılmasın).
            var inUse = await db.Products.AnyAsync(p => p.CategoryId == id, ct)
                        || await db.Categories.AnyAsync(c => c.ParentId == id, ct);
            if (inUse)
            {
                category.IsActive = false;
                audit.Record("category.deactivate", $"'{category.Name}' kullanımda olduğu için pasifleştirildi",
                    "Category", id.ToString());
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { deactivated = true });
            }

            db.Categories.Remove(category);
            audit.Record("category.delete", $"'{category.Name}' kategorisi silindi", "Category", id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    internal static string Slugify(string value)
    {
        var s = value.Trim().ToLowerInvariant()
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u')
            .Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        var chars = s.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
