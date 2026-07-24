using System.Text.Json;
using Marketplace.Cms.Api.Clients;
using Marketplace.Cms.Api.Components;
using Marketplace.Cms.Api.Domain;

namespace Marketplace.Cms.Api.Validation;

public record ValidationIssue(Guid ComponentId, string ComponentType, string Severity, string Message);

/// <summary>
/// İçerik bütünlüğü (R4): bileşenlerin işaret ettiği kayıtlar hâlâ var mı? Ürün ve kategori
/// referansları ORTAK KATALOG'a, indirim kodları Shopify read-model'ine karşı doğrulanır.
/// ("collection" bileşeni ortak katalogda KATEGORİ'ye karşılık gelir.) Silinmiş bir referans,
/// mobilde boş ekran demektir. Severity: "error" (yayını engeller) | "warning" (doğrulanamadı).
/// </summary>
public sealed class ContentValidator(IStoreDataClient storeData)
{
    public const string Error = "error";
    public const string Warning = "warning";

    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(PageVersion version, CancellationToken ct)
    {
        var issues = new List<ValidationIssue>();
        if (version.Components.Count == 0) return issues;

        var refs = await storeData.GetRefsAsync(ct);
        if (refs is null)
        {
            issues.Add(new ValidationIssue(Guid.Empty, "-", Warning,
                "Mağaza verisine ulaşılamadığı için referans doğrulaması yapılamadı."));
            return issues;
        }

        foreach (var c in version.Components.OrderBy(c => c.Position))
        {
            JsonElement s;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(c.SettingsJson) ? "{}" : c.SettingsJson);
                s = doc.RootElement.Clone();
            }
            catch
            {
                issues.Add(new ValidationIssue(c.Id, c.Type, Error, "Bileşen ayarları okunamadı (geçersiz JSON)."));
                continue;
            }

            switch (c.Type)
            {
                case ComponentTypes.Collection:
                    CheckCategory(c, s, "collectionId", refs, issues);
                    break;

                case ComponentTypes.ProductGrid:
                    var source = s.TryGetProperty("source", out var src) ? src.GetString() : null;
                    if (source == "collection")
                        CheckCategory(c, s, "collectionId", refs, issues);
                    else if (source == "manual" && s.TryGetProperty("productIds", out var pids) && pids.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in pids.EnumerateArray())
                        {
                            if (TryReadId(el, out var pid) && !refs.ProductIds.Contains(pid))
                                issues.Add(new ValidationIssue(c.Id, c.Type, Error,
                                    $"Ürün katalogda bulunamadı (silinmiş olabilir): {pid}"));
                        }
                    }
                    break;

                case ComponentTypes.Campaign:
                    if (s.TryGetProperty("discountCode", out var code) && code.GetString() is { Length: > 0 } dc
                        && !refs.DiscountCodes.Contains(dc))
                        issues.Add(new ValidationIssue(c.Id, c.Type, Error,
                            $"İndirim kodu bulunamadı: '{dc}'"));
                    break;

                case ComponentTypes.Banner:
                    var linkType = s.TryGetProperty("linkType", out var lt) ? lt.GetString() : null;
                    if (linkType is "product" or "collection" && s.TryGetProperty("linkTargetId", out var target))
                    {
                        if (TryReadId(target, out var tid))
                        {
                            var exists = linkType == "product" ? refs.ProductIds.Contains(tid) : refs.CategoryIds.Contains(tid);
                            if (!exists)
                                issues.Add(new ValidationIssue(c.Id, c.Type, Error,
                                    $"Banner hedefi bulunamadı ({linkType}): {tid}"));
                        }
                    }
                    break;
            }
        }

        return issues;
    }

    private static void CheckCategory(PageComponent c, JsonElement s, string key, StoreRefs refs, List<ValidationIssue> issues)
    {
        if (!s.TryGetProperty(key, out var v)) return;
        if (!TryReadId(v, out var id)) return;
        if (!refs.CategoryIds.Contains(id))
            issues.Add(new ValidationIssue(c.Id, c.Type, ContentValidator.Error,
                $"Kategori katalogda bulunamadı (silinmiş olabilir): {id}"));
    }

    /// <summary>Kimlik ayarlarda sayı veya metin (Guid) olarak gelebilir; metne indirger.</summary>
    private static bool TryReadId(JsonElement el, out string id)
    {
        id = "";
        switch (el.ValueKind)
        {
            case JsonValueKind.Number: id = el.GetRawText(); return true;
            case JsonValueKind.String: id = el.GetString() ?? ""; return id.Length > 0;
            default: return false;
        }
    }
}
