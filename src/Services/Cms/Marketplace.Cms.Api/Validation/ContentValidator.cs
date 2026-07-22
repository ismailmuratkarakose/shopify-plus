using System.Text.Json;
using Marketplace.Cms.Api.Clients;
using Marketplace.Cms.Api.Components;
using Marketplace.Cms.Api.Domain;

namespace Marketplace.Cms.Api.Validation;

public record ValidationIssue(Guid ComponentId, string ComponentType, string Severity, string Message);

/// <summary>
/// İçerik bütünlüğü: bileşenlerin işaret ettiği Shopify kayıtları (ürün, koleksiyon, indirim)
/// hâlâ var mı? Silinmiş bir koleksiyona bağlı bir alan, mobilde boş ekran demektir.
/// Severity: "error" (kırık referans, yayını engeller) | "warning" (doğrulanamadı).
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
                    CheckCollection(c, s, "collectionId", refs, issues);
                    break;

                case ComponentTypes.ProductGrid:
                    var source = s.TryGetProperty("source", out var src) ? src.GetString() : null;
                    if (source == "collection")
                        CheckCollection(c, s, "collectionId", refs, issues);
                    else if (source == "manual" && s.TryGetProperty("productIds", out var pids) && pids.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in pids.EnumerateArray())
                        {
                            if (TryReadId(el, out var pid) && !refs.ProductIds.Contains(pid))
                                issues.Add(new ValidationIssue(c.Id, c.Type, Error,
                                    $"Ürün mağazada bulunamadı (silinmiş olabilir): {pid}"));
                        }
                    }
                    break;

                case ComponentTypes.Campaign:
                    if (s.TryGetProperty("discountCode", out var code) && code.GetString() is { Length: > 0 } dc
                        && !refs.DiscountCodes.Contains(dc))
                        issues.Add(new ValidationIssue(c.Id, c.Type, Error,
                            $"İndirim kodu mağazada bulunamadı: '{dc}'"));
                    break;

                case ComponentTypes.Banner:
                    var linkType = s.TryGetProperty("linkType", out var lt) ? lt.GetString() : null;
                    if (linkType is "product" or "collection" && s.TryGetProperty("linkTargetId", out var target))
                    {
                        if (TryReadId(target, out var tid))
                        {
                            var exists = linkType == "product" ? refs.ProductIds.Contains(tid) : refs.CollectionIds.Contains(tid);
                            if (!exists)
                                issues.Add(new ValidationIssue(c.Id, c.Type, Error,
                                    $"Banner hedefi mağazada bulunamadı ({linkType}): {tid}"));
                        }
                    }
                    break;
            }
        }

        return issues;
    }

    private static void CheckCollection(PageComponent c, JsonElement s, string key, StoreRefs refs, List<ValidationIssue> issues)
    {
        if (!s.TryGetProperty(key, out var v)) return;
        if (!TryReadId(v, out var id)) return;
        if (!refs.CollectionIds.Contains(id))
            issues.Add(new ValidationIssue(c.Id, c.Type, ContentValidator.Error,
                $"Koleksiyon mağazada bulunamadı (silinmiş olabilir): {id}"));
    }

    /// <summary>Kimlik ayarlarda sayı veya metin olarak gelebilir; ikisini de kabul et.</summary>
    private static bool TryReadId(JsonElement el, out long id)
    {
        id = 0;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetInt64(out id),
            JsonValueKind.String => long.TryParse(el.GetString(), out id),
            _ => false
        };
    }
}
