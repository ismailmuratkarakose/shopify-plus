using System.Text.Json;

namespace Marketplace.Cms.Api.Components;

public sealed record ComponentTypeDefinition(
    string Type,
    string DisplayName,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<string> OptionalSettings,
    string Description);

/// <summary>
/// Desteklenen bileşen tipleri ve ayar şemaları. Sürükle-bırak tasarımcı bu listeyi okuyarak
/// paletini oluşturur; ekleme/güncelleme sırasında ayarlar buna göre doğrulanır.
/// </summary>
public static class ComponentTypes
{
    public const string Banner = "banner";
    public const string ProductGrid = "product_grid";
    public const string Collection = "collection";
    public const string Campaign = "campaign";
    public const string Popup = "popup";
    public const string Personalization = "personalization";
    public const string DynamicContent = "dynamic_content";

    private static readonly string[] ProductGridSources = ["manual", "collection", "recommendation"];
    private static readonly string[] PopupTriggers = ["onLoad", "onExit", "delay"];
    private static readonly string[] Scenarios =
    [
        "popular", "recently_viewed", "favorites", "similar", "for_you",
        "search_based", "cart_alternatives", "complementary", "cross_sell"
    ];

    public static readonly IReadOnlyDictionary<string, ComponentTypeDefinition> All =
        new Dictionary<string, ComponentTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [Banner] = new(Banner, "Banner Alanı",
                ["imageUrl"], ["title", "subtitle", "linkType", "linkTargetId"],
                "Görsel banner; ürün, koleksiyon veya kampanyaya yönlendirilebilir."),
            [ProductGrid] = new(ProductGrid, "Ürün Gösterim Alanı",
                ["source"], ["title", "productIds", "collectionId", "scenario", "limit"],
                "Ürün listesi. source: manual (productIds), collection (collectionId) veya recommendation (scenario)."),
            [Collection] = new(Collection, "Koleksiyon Alanı",
                ["collectionId"], ["title", "limit"],
                "Bir Shopify koleksiyonunun gösterimi."),
            [Campaign] = new(Campaign, "Kampanya Alanı",
                ["title"], ["discountCode", "imageUrl", "startsAt", "endsAt", "linkTargetId"],
                "Kampanya tanıtım alanı; Shopify indirim koduyla ilişkilendirilebilir."),
            [Popup] = new(Popup, "Popup Alanı",
                ["title", "trigger"], ["content", "imageUrl", "delaySeconds", "frequency", "linkTargetId"],
                "Açılır pencere. trigger: onLoad, onExit veya delay (delaySeconds ile)."),
            [Personalization] = new(Personalization, "Kişiselleştirme Bileşeni",
                ["scenario"], ["title", "limit"],
                "Kişiselleştirilmiş ürün önerisi; scenario öneri senaryosunu belirler."),
            [DynamicContent] = new(DynamicContent, "Dinamik İçerik Bileşeni",
                ["contentHtml"], ["title"],
                "Serbest içerik alanı (zengin metin / HTML).")
        };

    /// <summary>Bileşen tipini ve ayarlarını doğrular. Hata varsa açıklamasını döndürür.</summary>
    public static bool TryValidate(string type, string? settingsJson, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(type) || !All.TryGetValue(type, out var def))
        {
            error = $"Bilinmeyen bileşen tipi: '{type}'. Geçerli tipler: {string.Join(", ", All.Keys)}";
            return false;
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(settingsJson) ? "{}" : settingsJson!);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            error = $"Ayarlar geçerli bir JSON değil: {ex.Message}";
            return false;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Ayarlar bir JSON nesnesi olmalı.";
            return false;
        }

        foreach (var key in def.RequiredSettings)
        {
            if (!root.TryGetProperty(key, out var v) || IsEmpty(v))
            {
                error = $"'{def.DisplayName}' için zorunlu ayar eksik: '{key}'.";
                return false;
            }
        }

        // Tipe özel kural kontrolleri
        switch (def.Type)
        {
            case ProductGrid:
                var source = root.GetProperty("source").GetString() ?? "";
                if (!ProductGridSources.Contains(source))
                {
                    error = $"'source' şunlardan biri olmalı: {string.Join(", ", ProductGridSources)}";
                    return false;
                }
                if (source == "manual" && (!root.TryGetProperty("productIds", out var pid) || pid.ValueKind != JsonValueKind.Array || pid.GetArrayLength() == 0))
                {
                    error = "source='manual' için en az bir ürün içeren 'productIds' dizisi gerekli.";
                    return false;
                }
                if (source == "collection" && (!root.TryGetProperty("collectionId", out var cid) || IsEmpty(cid)))
                {
                    error = "source='collection' için 'collectionId' gerekli.";
                    return false;
                }
                if (source == "recommendation" && !ValidScenario(root, out error))
                    return false;
                break;

            case Popup:
                var trigger = root.GetProperty("trigger").GetString() ?? "";
                if (!PopupTriggers.Contains(trigger))
                {
                    error = $"'trigger' şunlardan biri olmalı: {string.Join(", ", PopupTriggers)}";
                    return false;
                }
                if (trigger == "delay" && (!root.TryGetProperty("delaySeconds", out var ds) || ds.ValueKind != JsonValueKind.Number))
                {
                    error = "trigger='delay' için sayısal 'delaySeconds' gerekli.";
                    return false;
                }
                break;

            case Personalization:
                if (!ValidScenario(root, out error))
                    return false;
                break;
        }

        return true;
    }

    private static bool ValidScenario(JsonElement root, out string error)
    {
        error = "";
        var scenario = root.TryGetProperty("scenario", out var s) ? s.GetString() ?? "" : "";
        if (!Scenarios.Contains(scenario))
        {
            error = $"'scenario' şunlardan biri olmalı: {string.Join(", ", Scenarios)}";
            return false;
        }
        return true;
    }

    private static bool IsEmpty(JsonElement v) =>
        v.ValueKind == JsonValueKind.Null ||
        v.ValueKind == JsonValueKind.Undefined ||
        (v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString()));
}
