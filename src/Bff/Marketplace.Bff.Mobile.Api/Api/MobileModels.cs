using System.Security.Claims;
using Marketplace.Bff.Mobile.Api.Cart;
using Marketplace.BuildingBlocks.MultiTenancy;

namespace Marketplace.Bff.Mobile.Api.Api;

// --- Katalog (master + satıcı kıyası) ---
public record MobileProductDto(Guid Id, string Barcode, string Title, string? Brand, int SellerCount, decimal? MinPrice);
public record SellerDto(Guid OfferId, Guid MerchantId, string? Sku, decimal Price, string Currency);
public record MobileProductDetailDto(
    Guid Id, string Barcode, string Title, string? Description, string? Brand, string? ImageUrl,
    IReadOnlyList<SellerDto> Sellers);

// --- Sepet ---
public record CartItemDto(Guid OfferId, Guid ProductId, Guid MerchantId, string Barcode, string Title,
    decimal UnitPrice, int Quantity, decimal LineTotal);
public record CartViewDto(IReadOnlyList<CartItemDto> Items, string? Currency, decimal TotalAmount, int ItemCount, int MerchantCount);

/// <summary>Sepete ekleme: hangi ürünü (master) hangi satıcıdan (merchant) kaç adet.</summary>
public record AddToCartRequest(Guid ProductId, Guid MerchantId, int Quantity);
public record UpdateQtyRequest(int Quantity);

// --- Checkout: sepet satıcıya göre bölünür → merchant başına bir sipariş ---
public record CheckoutOrderDto(Guid OrderId, Guid MerchantId, string Status, decimal TotalAmount, string Currency, string? StatusReason);
public record CheckoutResultDto(IReadOnlyList<CheckoutOrderDto> Orders);

public static class CartMapping
{
    public static CartViewDto ToView(this CartState state)
    {
        var items = state.Lines
            .Select(l => new CartItemDto(l.OfferId, l.ProductId, l.MerchantId, l.Barcode, l.Title,
                l.UnitPrice, l.Quantity, l.UnitPrice * l.Quantity))
            .ToList();

        return new CartViewDto(
            items,
            state.Lines.FirstOrDefault()?.Currency,
            items.Sum(i => i.LineTotal),
            items.Sum(i => i.Quantity),
            state.Lines.Select(l => l.MerchantId).Distinct().Count());
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
