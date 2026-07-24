using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Mobile.Api.Domain;

/// <summary>
/// Müşterinin favori ürünü (ortak katalog kartına işaret eder — R4). Kullanıcı JWT'deki
/// kimlikle (sub) tanımlanır; mağaza boyutu YOKTUR — favori, kart seviyesindedir.
/// </summary>
public class FavoriteProduct : AuditableEntity
{
    public string UserRef { get; set; } = default!;
    public Guid ProductId { get; set; }
}

/// <summary>Son görüntülenen ürün kartı (kullanıcı başına sınırlı sayıda tutulur).</summary>
public class RecentlyViewedProduct : AuditableEntity
{
    public string UserRef { get; set; } = default!;
    public Guid ProductId { get; set; }
    public DateTimeOffset ViewedAt { get; set; } = DateTimeOffset.UtcNow;
}
