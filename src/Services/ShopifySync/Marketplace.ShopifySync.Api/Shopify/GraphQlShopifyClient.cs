using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Marketplace.ShopifySync.Api.Shopify;

/// <summary>
/// Gerçek Shopify Admin GraphQL API implementasyonu (go-forward). Kimlik bilgileri
/// merchant başına çözülür; token X-Shopify-Access-Token header'ında gider.
///
/// NOT: Ürün fiyatı/SKU Shopify'da variant üzerinde tutulur. Bu implementasyon ürünü
/// productCreate/productUpdate ile oluşturur/günceller; variant fiyat/SKU push'u gerçek
/// bir mağaza bağlandığında productVariantsBulkUpdate ile tamamlanacaktır (Faz 2b+).
/// </summary>
public sealed partial class GraphQlShopifyClient : IShopifyClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GraphQlShopifyClient> _logger;
    private readonly string _apiVersion;

    public GraphQlShopifyClient(HttpClient http, IConfiguration config, ILogger<GraphQlShopifyClient> logger)
    {
        _http = http;
        _logger = logger;
        _apiVersion = config["Shopify:ApiVersion"] ?? "2025-01";
    }

    public async Task<ShopifyProductRef> UpsertProductAsync(
        ShopifyStoreCredentials store,
        ShopifyProductPush product,
        long? existingShopifyProductId,
        CancellationToken ct)
    {
        var url = $"https://{store.ShopDomain}/admin/api/{_apiVersion}/graphql.json";

        string mutation;
        object variables;
        if (existingShopifyProductId is null)
        {
            mutation = """
                mutation productCreate($input: ProductInput!) {
                  productCreate(input: $input) {
                    product { id }
                    userErrors { field message }
                  }
                }
                """;
            variables = new { input = new { title = product.Title, descriptionHtml = product.Description ?? "" } };
        }
        else
        {
            mutation = """
                mutation productUpdate($input: ProductInput!) {
                  productUpdate(input: $input) {
                    product { id }
                    userErrors { field message }
                  }
                }
                """;
            variables = new
            {
                input = new
                {
                    id = $"gid://shopify/Product/{existingShopifyProductId}",
                    title = product.Title,
                    descriptionHtml = product.Description ?? ""
                }
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { query = mutation, variables })
        };
        request.Headers.Add("X-Shopify-Access-Token", store.AccessToken);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement.GetProperty("data");
        var op = existingShopifyProductId is null ? "productCreate" : "productUpdate";
        var result = root.GetProperty(op);

        if (result.TryGetProperty("userErrors", out var errors) && errors.GetArrayLength() > 0)
        {
            var msg = string.Join("; ", errors.EnumerateArray().Select(e => e.GetProperty("message").GetString()));
            throw new InvalidOperationException($"Shopify {op} hatası: {msg}");
        }

        var gid = result.GetProperty("product").GetProperty("id").GetString()!;
        var id = long.Parse(ProductGidRegex().Match(gid).Groups[1].Value);
        _logger.LogInformation("Shopify {Op} OK: {Sku} -> {Id}", op, product.Sku, id);
        return new ShopifyProductRef(id);
    }

    // Read senkronu (Faz B): gerçek GraphQL products/collections sorguları bir mağaza bağlandığında
    // eklenecek (bulk operations / pagination). Şimdilik simulator modunda çalışılır.
    public Task<IReadOnlyList<ShopifyProductData>> GetProductsAsync(ShopifyStoreCredentials store, CancellationToken ct)
        => throw new NotImplementedException("GraphQL ürün okuma Faz B'de simulator ile çalışıyor; gerçek sorgu ileride.");

    public Task<IReadOnlyList<ShopifyCollectionData>> GetCollectionsAsync(ShopifyStoreCredentials store, CancellationToken ct)
        => throw new NotImplementedException("GraphQL koleksiyon okuma Faz B'de simulator ile çalışıyor; gerçek sorgu ileride.");

    [GeneratedRegex(@"gid://shopify/Product/(\d+)")]
    private static partial Regex ProductGidRegex();
}
