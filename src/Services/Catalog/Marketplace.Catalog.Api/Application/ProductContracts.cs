using FluentValidation;
using Marketplace.BuildingBlocks.Results;
using MediatR;

namespace Marketplace.Catalog.Api.Application;

// --- DTO'lar ---

/// <summary>Global ürün master'ı.</summary>
public record ProductMasterDto(
    Guid Id, string Barcode, string Title, string? Description, string? Brand, Guid? CategoryId, string? ImageUrl);

/// <summary>Bir merchant'ın teklifi (master bilgisiyle zenginleştirilmiş).</summary>
public record OfferDto(
    Guid Id, Guid ProductId, string Barcode, string Title, string? Sku,
    decimal Price, string Currency, bool IsActive, string Source);

/// <summary>Ürün detayında bir satıcının teklifi (satıcı kıyası için).</summary>
public record SellerOfferDto(Guid OfferId, Guid MerchantId, string? Sku, decimal Price, string Currency, bool IsActive);

/// <summary>Master + tüm satıcı teklifleri.</summary>
public record ProductWithOffersDto(ProductMasterDto Product, IReadOnlyList<SellerOfferDto> Offers);

/// <summary>Katalog listeleme öğesi: master + teklif özeti.</summary>
public record ProductListItemDto(Guid Id, string Barcode, string Title, string? Brand, int OfferCount, decimal? MinPrice);

// --- Commands / Queries ---

/// <summary>Teklif oluştur/ekle: barkod master'ı yoksa oluşturulur, sonra bu merchant için offer açılır.</summary>
public record CreateOfferCommand(
    string Barcode, string Title, string? Description, string? Brand, Guid? CategoryId, string? ImageUrl,
    string? Sku, decimal Price, string Currency) : IRequest<Result<OfferDto>>;

public record UpdateOfferCommand(Guid Id, decimal Price, bool IsActive) : IRequest<Result<OfferDto>>;

public record GetMyOffersQuery(int Page = 1, int PageSize = 20) : IRequest<Result<IReadOnlyList<OfferDto>>>;
public record GetOfferByIdQuery(Guid Id) : IRequest<Result<OfferDto>>;

public record GetProductsQuery(int Page = 1, int PageSize = 20, string? Search = null) : IRequest<Result<IReadOnlyList<ProductListItemDto>>>;
public record GetProductByIdQuery(Guid Id) : IRequest<Result<ProductWithOffersDto>>;
public record GetProductByBarcodeQuery(string Barcode) : IRequest<Result<ProductWithOffersDto>>;

// --- Validation ---

public sealed class CreateOfferValidator : AbstractValidator<CreateOfferCommand>
{
    public CreateOfferValidator()
    {
        RuleFor(x => x.Barcode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}
