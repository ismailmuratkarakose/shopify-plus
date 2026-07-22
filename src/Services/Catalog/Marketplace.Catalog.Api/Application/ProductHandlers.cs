using FluentValidation;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.BuildingBlocks.Results;
using Marketplace.Catalog.Api.Domain;
using Marketplace.Catalog.Api.Infrastructure;
using Marketplace.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Application;

/// <summary>
/// Teklif oluştur: barkodla master'ı bul, yoksa oluştur (ilk satıcı master'ı doğurur),
/// sonra bu merchant için offer aç. Master zaten varsa ürün bilgisi güncellenmez (mevcut korunur).
/// </summary>
public sealed class CreateOfferHandler : IRequestHandler<CreateOfferCommand, Result<OfferDto>>
{
    private readonly CatalogDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IValidator<CreateOfferCommand> _validator;

    public CreateOfferHandler(CatalogDbContext db, ITenantContext tenant, IValidator<CreateOfferCommand> validator)
    {
        _db = db;
        _tenant = tenant;
        _validator = validator;
    }

    public async Task<Result<OfferDto>> Handle(CreateOfferCommand request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Result.Failure<OfferDto>(Error.Validation("offer.invalid", validation.Errors[0].ErrorMessage));

        if (_tenant.TenantId is null)
            return Result.Failure<OfferDto>(Error.Unauthorized("tenant.missing", "İstek bir merchant kapsamı taşımıyor."));

        // Master global; barkodla bul veya oluştur.
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Barcode == request.Barcode, ct);
        if (product is null)
        {
            product = new Product
            {
                Barcode = request.Barcode,
                Title = request.Title,
                Description = request.Description,
                Brand = request.Brand,
                CategoryId = request.CategoryId,
                ImageUrl = request.ImageUrl
            };
            _db.Products.Add(product);
        }

        // Merchant başına master için tek offer.
        var offerExists = await _db.Offers.AnyAsync(o => o.ProductId == product.Id, ct);
        if (offerExists)
            return Result.Failure<OfferDto>(
                Error.Conflict("offer.exists", $"Bu ürün için zaten bir teklifiniz var (barkod {request.Barcode})."));

        var offer = new Offer
        {
            Product = product,
            Sku = request.Sku,
            Price = request.Price,
            Currency = request.Currency,
            Source = ProductSource.Marketplace
        };
        _db.Offers.Add(offer);

        // Event iş verisiyle aynı transaction'da outbox'a yazılır. TenantId entity'den değil _tenant'tan.
        _db.EnqueueIntegrationEvent(new ProductCreatedIntegrationEvent
        {
            TenantId = _tenant.TenantId,
            ProductId = product.Id,
            OfferId = offer.Id,
            Barcode = product.Barcode,
            Sku = offer.Sku ?? product.Barcode,
            Title = product.Title,
            Description = product.Description,
            Price = offer.Price,
            Currency = offer.Currency,
            Source = ProductSource.Marketplace
        });

        await _db.SaveChangesAsync(ct);

        return Result.Success(Map(offer, product));
    }

    internal static OfferDto Map(Offer o, Product p) =>
        new(o.Id, p.Id, p.Barcode, p.Title, o.Sku, o.Price, o.Currency, o.IsActive, o.Source);
}

public sealed class UpdateOfferHandler : IRequestHandler<UpdateOfferCommand, Result<OfferDto>>
{
    private readonly CatalogDbContext _db;
    public UpdateOfferHandler(CatalogDbContext db) => _db = db;

    public async Task<Result<OfferDto>> Handle(UpdateOfferCommand request, CancellationToken ct)
    {
        if (request.Price < 0)
            return Result.Failure<OfferDto>(Error.Validation("offer.invalid_price", "Fiyat negatif olamaz."));

        var offer = await _db.Offers.Include(o => o.Product).FirstOrDefaultAsync(o => o.Id == request.Id, ct);
        if (offer is null)
            return Result.Failure<OfferDto>(Error.NotFound("offer.not_found", $"Teklif bulunamadı: {request.Id}"));

        offer.Price = request.Price;
        offer.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);

        return Result.Success(CreateOfferHandler.Map(offer, offer.Product));
    }
}

public sealed class GetMyOffersHandler : IRequestHandler<GetMyOffersQuery, Result<IReadOnlyList<OfferDto>>>
{
    private readonly CatalogDbContext _db;
    public GetMyOffersHandler(CatalogDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<OfferDto>>> Handle(GetMyOffersQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);

        var items = await _db.Offers
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(o => new OfferDto(o.Id, o.ProductId, o.Product.Barcode, o.Product.Title, o.Sku,
                o.Price, o.Currency, o.IsActive, o.Source))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<OfferDto>>(items);
    }
}

public sealed class GetOfferByIdHandler : IRequestHandler<GetOfferByIdQuery, Result<OfferDto>>
{
    private readonly CatalogDbContext _db;
    public GetOfferByIdHandler(CatalogDbContext db) => _db = db;

    public async Task<Result<OfferDto>> Handle(GetOfferByIdQuery request, CancellationToken ct)
    {
        var offer = await _db.Offers
            .Where(o => o.Id == request.Id)
            .Select(o => new OfferDto(o.Id, o.ProductId, o.Product.Barcode, o.Product.Title, o.Sku,
                o.Price, o.Currency, o.IsActive, o.Source))
            .FirstOrDefaultAsync(ct);

        return offer is null
            ? Result.Failure<OfferDto>(Error.NotFound("offer.not_found", $"Teklif bulunamadı: {request.Id}"))
            : Result.Success(offer);
    }
}

/// <summary>Global ürün kataloğu: master + aktif teklif özeti (tüm satıcılar; tenant filtresi yok).</summary>
public sealed class GetProductsHandler : IRequestHandler<GetProductsQuery, Result<IReadOnlyList<ProductListItemDto>>>
{
    private readonly CatalogDbContext _db;
    public GetProductsHandler(CatalogDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<ProductListItemDto>>> Handle(GetProductsQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);

        // IgnoreQueryFilters: teklif özetinde tüm satıcılar sayılır (yalnızca istek sahibinin değil).
        var q = _db.Products.IgnoreQueryFilters().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
            q = q.Where(p => p.Title.Contains(request.Search) || p.Barcode == request.Search);

        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(p => new ProductListItemDto(
                p.Id, p.Barcode, p.Title, p.Brand,
                p.Offers.Count(o => o.IsActive),
                p.Offers.Where(o => o.IsActive).Min(o => (decimal?)o.Price)))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ProductListItemDto>>(items);
    }
}

public sealed class GetProductByIdHandler : IRequestHandler<GetProductByIdQuery, Result<ProductWithOffersDto>>
{
    private readonly CatalogDbContext _db;
    public GetProductByIdHandler(CatalogDbContext db) => _db = db;

    public Task<Result<ProductWithOffersDto>> Handle(GetProductByIdQuery request, CancellationToken ct)
        => ProductQueries.WithOffers(_db, p => p.Id == request.Id, $"Ürün bulunamadı: {request.Id}", ct);
}

public sealed class GetProductByBarcodeHandler : IRequestHandler<GetProductByBarcodeQuery, Result<ProductWithOffersDto>>
{
    private readonly CatalogDbContext _db;
    public GetProductByBarcodeHandler(CatalogDbContext db) => _db = db;

    public Task<Result<ProductWithOffersDto>> Handle(GetProductByBarcodeQuery request, CancellationToken ct)
        => ProductQueries.WithOffers(_db, p => p.Barcode == request.Barcode, $"Ürün bulunamadı: {request.Barcode}", ct);
}

internal static class ProductQueries
{
    public static async Task<Result<ProductWithOffersDto>> WithOffers(
        CatalogDbContext db,
        System.Linq.Expressions.Expression<Func<Product, bool>> predicate,
        string notFoundMessage,
        CancellationToken ct)
    {
        var product = await db.Products.Where(predicate)
            .Select(p => new ProductMasterDto(p.Id, p.Barcode, p.Title, p.Description, p.Brand, p.CategoryId, p.ImageUrl))
            .FirstOrDefaultAsync(ct);

        if (product is null)
            return Result.Failure<ProductWithOffersDto>(Error.NotFound("product.not_found", notFoundMessage));

        // Tüm satıcıların aktif teklifleri (tenant filtresi yok), ucuzdan pahalıya.
        var offers = await db.Offers.IgnoreQueryFilters()
            .Where(o => o.ProductId == product.Id && o.IsActive)
            .OrderBy(o => o.Price)
            .Select(o => new SellerOfferDto(o.Id, o.TenantId, o.Sku, o.Price, o.Currency, o.IsActive))
            .ToListAsync(ct);

        return Result.Success(new ProductWithOffersDto(product, offers));
    }
}
