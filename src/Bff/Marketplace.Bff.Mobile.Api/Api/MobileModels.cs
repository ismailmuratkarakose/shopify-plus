using System.Security.Claims;
using Marketplace.Bff.Mobile.Api.Cart;
using Marketplace.BuildingBlocks.MultiTenancy;

namespace Marketplace.Bff.Mobile.Api.Api;

// --- Mobil-yüzlü sözleşmeler (tek ekran = tek çağrı hedefiyle sadeleştirilmiş) ---

public record MobileProductDto(
    Guid Id,
    string Sku,
    string Title,
    string? Description,
    decimal Price,
    string Currency,
    int Available,
    bool InStock);

public record CartItemDto(Guid ProductId, string Sku, string Title, decimal UnitPrice, int Quantity, decimal LineTotal);
public record CartViewDto(IReadOnlyList<CartItemDto> Items, string? Currency, decimal TotalAmount, int ItemCount);

public record AddToCartRequest(Guid ProductId, int Quantity);
public record UpdateQtyRequest(int Quantity);

public record CheckoutResponse(Guid OrderId, string Status, decimal TotalAmount, string Currency, string? StatusReason);

public static class CartMapping
{
    public static CartViewDto ToView(this CartState state)
    {
        var items = state.Lines
            .Select(l => new CartItemDto(l.ProductId, l.Sku, l.Title, l.UnitPrice, l.Quantity, l.UnitPrice * l.Quantity))
            .ToList();

        return new CartViewDto(
            items,
            state.Lines.FirstOrDefault()?.Currency,
            items.Sum(i => i.LineTotal),
            items.Sum(i => i.Quantity));
    }
}

/// <summary>Sepet Redis anahtarını kullanıcı + tenant kapsamından üretir.</summary>
public static class CartKey
{
    public static string For(ClaimsPrincipal user, ITenantContext tenant)
    {
        var sub = user.FindFirstValue("sub")
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? "anonymous";
        var scope = tenant.TenantId?.ToString() ?? "none";
        return $"cart:{scope}:{sub}";
    }
}
