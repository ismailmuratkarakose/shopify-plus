using Marketplace.Catalog.Api.Domain;
using Marketplace.Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Application;

public record UpsertInput(string? Barcode, string? Title, decimal Price,
    string? Description = null, string? Brand = null, Guid? CategoryId = null, string? ImageUrl = null,
    string? Sku = null, decimal? CompareAtPrice = null, int StockQuantity = 0);

public record UpsertOutcome(Product Product, Offer Offer, bool CreatedMaster, bool CreatedOffer);

/// <summary>
/// Tek noktadan "barkodla ürün ekle/güncelle" iş kuralı — manuel uç (R2) ve Excel içeri aktarım (R3)
/// aynı kuralı paylaşır: barkod master'ı belirler; kart yoksa oluşur, varsa mağaza karta katılır
/// ve kartın alanları DEĞİŞMEZ. Mağaza başına master için tek teklif.
/// SaveChanges ÇAĞIRMAZ — çağıran toplu işlem yapabilir (Excel'de satır satır kaydetmek yavaş olur).
/// </summary>
public sealed class ProductUpsertService(CatalogDbContext db)
{
    /// <summary>Doğrulama hatasında (hataKodu, mesaj) döner; başarıda outcome.</summary>
    public async Task<(UpsertOutcome? Outcome, string? Error)> UpsertAsync(
        UpsertInput input, string source, CancellationToken ct)
    {
        var barcode = input.Barcode?.Trim();
        if (string.IsNullOrWhiteSpace(barcode) || barcode.Length is < 8 or > 64)
            return (null, "Geçerli bir barkod (GTIN/EAN/UPC, 8-64 karakter) gerekli.");
        if (string.IsNullOrWhiteSpace(input.Title))
            return (null, "Ürün adı gerekli.");
        if (input.Price <= 0)
            return (null, "Fiyat sıfırdan büyük olmalı.");
        if (input.StockQuantity < 0)
            return (null, "Stok negatif olamaz.");

        // Önce izlenen (bu toplu işlemde az önce eklenmiş) kartlara bak — Excel'de aynı barkod
        // iki satırda geçerse ikinci satır DB'ye gitmeden karta bağlanır.
        var product = db.Products.Local.FirstOrDefault(x => x.Barcode == barcode)
                      ?? await db.Products.FirstOrDefaultAsync(x => x.Barcode == barcode, ct);
        var createdMaster = false;
        if (product is null)
        {
            product = new Product
            {
                Barcode = barcode,
                Title = input.Title!.Trim(),
                Description = input.Description,
                Brand = input.Brand,
                CategoryId = input.CategoryId,
                ImageUrl = input.ImageUrl,
                CreatedSource = source
            };
            db.Products.Add(product);
            createdMaster = true;
        }

        var offer = db.Offers.Local.FirstOrDefault(o => o.ProductId == product.Id)
                    ?? await db.Offers.FirstOrDefaultAsync(o => o.ProductId == product.Id, ct);
        var createdOffer = offer is null;
        if (offer is null)
        {
            offer = new Offer { ProductId = product.Id, Product = product, Source = source };
            db.Offers.Add(offer);
        }

        offer.Price = input.Price;
        offer.CompareAtPrice = input.CompareAtPrice;
        offer.StockQuantity = input.StockQuantity;
        offer.Sku = input.Sku;
        offer.Source = source;
        offer.IsActive = true;

        return (new UpsertOutcome(product, offer, createdMaster, createdOffer), null);
    }
}
