using Marketplace.BuildingBlocks.Auditing;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Catalog.Api.Application;
using Marketplace.Catalog.Api.Domain;
using Marketplace.Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Api;

public record ImportRowError(int Row, string Error);
public record ImportRowWarning(int Row, string Warning);
public record ImportResult(int TotalRows, int Imported, int NewProducts, int Failed,
    IReadOnlyList<ImportRowError> Errors, IReadOnlyList<ImportRowWarning> Warnings);

/// <summary>
/// Mağaza katalogunun toplu içeri aktarımı (R3): CSV veya XLSX. Shopify kullanmayan mağazanın
/// ana yolu budur. Satır bazlı doğrulama; hatalı satır diğerlerini ENGELLEMEZ (kısmi başarı),
/// rapor satır numarasıyla döner. Aynı dosya tekrar yüklenirse fiyat/stok güncellenir (idempotent).
/// </summary>
public static class ImportEndpoints
{
    private const int MaxRows = 5000;
    private const long MaxBytes = 2 * 1024 * 1024;

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/store/products/import").WithTags("StoreImport")
            .RequireAuthorization(Policies.StoreManage);

        // Şablon: mağazanın dolduracağı örnek CSV (kolon adları Türkçe de olabilir — parser tolere eder).
        group.MapGet("/template", () =>
        {
            const string csv = "Barcode;Title;Price;StockQuantity;Brand;Category;Description;Sku;CompareAtPrice;ImageUrl\n" +
                               "8690000000015;Örnek Ürün;149,90;25;MarkaX;elektronik;Açıklama;SKU-1;199,90;https://ornek/gorsel.jpg\n";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "urun-sablonu.csv");
        }).AllowAnonymous();

        group.MapPost("/", async (HttpRequest request, CatalogDbContext db, ProductUpsertService upsert,
            IStoreContext scope, IAuditLogger audit, CancellationToken ct) =>
        {
            if (scope.StoreId is null)
                return Results.Problem("Mağaza kapsamı yok. (Platform personeli X-Acting-Store başlığı kullanmalı.)",
                    statusCode: StatusCodes.Status400BadRequest, title: "store.missing");
            if (!request.HasFormContentType)
                return Results.Problem("Dosya multipart/form-data olarak gönderilmeli.",
                    statusCode: StatusCodes.Status400BadRequest, title: "import.invalid_request");

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.Problem("Dosya bulunamadı.", statusCode: StatusCodes.Status400BadRequest,
                    title: "import.no_file");
            if (file.Length > MaxBytes)
                return Results.Problem($"Dosya çok büyük ({file.Length} bayt). Üst sınır: {MaxBytes} bayt.",
                    statusCode: StatusCodes.Status400BadRequest, title: "import.too_large");

            List<ImportRow> rows;
            try
            {
                await using var stream = file.OpenReadStream();
                rows = ImportParsers.Parse(stream, file.FileName);
            }
            catch (InvalidDataException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest,
                    title: "import.parse_error");
            }

            if (rows.Count == 0)
                return Results.Problem("Dosyada veri satırı yok.", statusCode: StatusCodes.Status400BadRequest,
                    title: "import.empty");
            if (rows.Count > MaxRows)
                return Results.Problem($"Çok fazla satır ({rows.Count}). Üst sınır: {MaxRows}. Dosyayı bölün.",
                    statusCode: StatusCodes.Status400BadRequest, title: "import.too_many_rows");

            // Kategori eşleme: slug VEYA ad üzerinden, tek sorguda sözlüğe alınır.
            var categories = await db.Categories.Where(c => c.IsActive).ToListAsync(ct);
            var bySlug = categories.ToDictionary(c => c.Slug, c => c.Id, StringComparer.OrdinalIgnoreCase);
            var byName = categories
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            var errors = new List<ImportRowError>();
            var warnings = new List<ImportRowWarning>();
            var imported = 0; var newProducts = 0;

            foreach (var row in rows)
            {
                string Get(string col) => row.Fields.GetValueOrDefault(col, "");

                if (!ImportParsers.TryParseDecimal(Get("price"), out var price))
                {
                    errors.Add(new ImportRowError(row.RowNumber, $"Fiyat okunamadı: '{Get("price")}'"));
                    continue;
                }

                decimal? compareAt = null;
                if (!string.IsNullOrWhiteSpace(Get("compareatprice")))
                {
                    if (ImportParsers.TryParseDecimal(Get("compareatprice"), out var cap)) compareAt = cap;
                    else warnings.Add(new ImportRowWarning(row.RowNumber,
                        $"Karşılaştırma fiyatı okunamadı, boş bırakıldı: '{Get("compareatprice")}'"));
                }

                var stock = 0;
                if (!string.IsNullOrWhiteSpace(Get("stockquantity")) &&
                    !int.TryParse(Get("stockquantity"), out stock))
                {
                    warnings.Add(new ImportRowWarning(row.RowNumber,
                        $"Stok okunamadı, 0 kabul edildi: '{Get("stockquantity")}'"));
                    stock = 0;
                }

                Guid? categoryId = null;
                var catText = Get("category");
                if (!string.IsNullOrWhiteSpace(catText))
                {
                    if (bySlug.TryGetValue(catText, out var cid) || byName.TryGetValue(catText, out cid))
                        categoryId = cid;
                    else
                        // Kategori hatası satırı DÜŞÜRMEZ: ürün kategorisiz aktarılır, rapor uyarır.
                        warnings.Add(new ImportRowWarning(row.RowNumber,
                            $"Kategori bulunamadı, ürün kategorisiz aktarıldı: '{catText}'"));
                }

                var input = new UpsertInput(Get("barcode"), Get("title"), price,
                    NullIfEmpty(Get("description")), NullIfEmpty(Get("brand")), categoryId,
                    NullIfEmpty(Get("imageurl")), NullIfEmpty(Get("sku")), compareAt, stock);

                var (outcome, error) = await upsert.UpsertAsync(input, ProductSource.Excel, ct);
                if (error is not null)
                {
                    errors.Add(new ImportRowError(row.RowNumber, error));
                    continue;
                }

                imported++;
                if (outcome!.CreatedMaster) newProducts++;
            }

            // Tek transaction: geçerli satırların tamamı birlikte kaydedilir; denetim kaydı da aynı işlemde.
            audit.Record("catalog.import",
                $"'{file.FileName}' içeri aktarıldı: {imported}/{rows.Count} satır başarılı, " +
                $"{newProducts} yeni ürün kartı, {errors.Count} hatalı satır");
            await db.SaveChangesAsync(ct);

            return Results.Ok(new ImportResult(rows.Count, imported, newProducts, errors.Count, errors, warnings));
        }).DisableAntiforgery();

        return app;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
