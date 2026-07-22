using System.Text.Json;
using StackExchange.Redis;

namespace Marketplace.Bff.Mobile.Api.Cart;

/// <summary>Redis'te saklanan sepet satırı. Fiyat/başlık, ürün eklendiği andaki değerin anlık kopyasıdır.</summary>
public sealed class CartLine
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "";
    public int Quantity { get; set; }
}

/// <summary>Bir kullanıcının sepetinin tamamı (Redis'te tek JSON değeri olarak saklanır).</summary>
public sealed class CartState
{
    public List<CartLine> Lines { get; set; } = [];
}

public interface ICartStore
{
    Task<CartState> GetAsync(string key, CancellationToken ct);
    Task SaveAsync(string key, CartState state, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

public sealed class RedisCartStore : ICartStore
{
    private readonly IDatabase _db;
    private readonly TimeSpan _ttl;

    public RedisCartStore(IConnectionMultiplexer redis, IConfiguration config)
    {
        _db = redis.GetDatabase();
        var days = config.GetValue<int?>("Redis:CartTtlDays") ?? 7;
        _ttl = TimeSpan.FromDays(days);
    }

    public async Task<CartState> GetAsync(string key, CancellationToken ct)
    {
        var value = await _db.StringGetAsync(key);
        if (value.IsNullOrEmpty)
            return new CartState();
        return JsonSerializer.Deserialize<CartState>((string)value!) ?? new CartState();
    }

    public Task SaveAsync(string key, CartState state, CancellationToken ct)
    {
        // Sepet boşsa anahtarı tut ama sıfırla; TTL ile temizlenir.
        var json = JsonSerializer.Serialize(state);
        return _db.StringSetAsync(key, json, _ttl);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
        => _db.KeyDeleteAsync(key);
}
