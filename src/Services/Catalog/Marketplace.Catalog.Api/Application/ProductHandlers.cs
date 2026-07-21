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

public sealed class CreateProductHandler : IRequestHandler<CreateProductCommand, Result<ProductDto>>
{
    private readonly CatalogDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IValidator<CreateProductCommand> _validator;

    public CreateProductHandler(
        CatalogDbContext db,
        ITenantContext tenant,
        IValidator<CreateProductCommand> validator)
    {
        _db = db;
        _tenant = tenant;
        _validator = validator;
    }

    public async Task<Result<ProductDto>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Result.Failure<ProductDto>(
                Error.Validation("product.invalid", validation.Errors[0].ErrorMessage));

        if (_tenant.TenantId is null)
            return Result.Failure<ProductDto>(
                Error.Unauthorized("tenant.missing", "İstek bir merchant kapsamı taşımıyor."));

        var exists = await _db.Products.AnyAsync(p => p.Sku == request.Sku, ct);
        if (exists)
            return Result.Failure<ProductDto>(
                Error.Conflict("product.sku_exists", $"'{request.Sku}' SKU zaten mevcut."));

        var product = new Product
        {
            Sku = request.Sku,
            Title = request.Title,
            Description = request.Description,
            CategoryId = request.CategoryId,
            Price = request.Price,
            Currency = request.Currency,
            Source = ProductSource.Marketplace
        };

        _db.Products.Add(product);

        // Event, iş verisiyle aynı transaction'da outbox'a yazılır (atomik). Dispatcher sonradan yayınlar.
        _db.EnqueueIntegrationEvent(new ProductCreatedIntegrationEvent
        {
            TenantId = product.TenantId,
            ProductId = product.Id,
            Sku = product.Sku,
            Title = product.Title,
            Price = product.Price,
            Currency = product.Currency
        });

        await _db.SaveChangesAsync(ct);

        return Result.Success(Map(product));
    }

    private static ProductDto Map(Product p) => new(
        p.Id, p.Sku, p.Title, p.Description, p.CategoryId,
        p.Price, p.Currency, p.IsActive, p.ShopifyProductId, p.Source);
}

public sealed class GetProductsHandler : IRequestHandler<GetProductsQuery, Result<IReadOnlyList<ProductDto>>>
{
    private readonly CatalogDbContext _db;

    public GetProductsHandler(CatalogDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<ProductDto>>> Handle(GetProductsQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);

        var items = await _db.Products
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(p => new ProductDto(
                p.Id, p.Sku, p.Title, p.Description, p.CategoryId,
                p.Price, p.Currency, p.IsActive, p.ShopifyProductId, p.Source))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ProductDto>>(items);
    }
}
