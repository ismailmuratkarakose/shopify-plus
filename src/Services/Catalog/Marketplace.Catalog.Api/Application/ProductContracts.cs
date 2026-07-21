using FluentValidation;
using Marketplace.BuildingBlocks.Results;
using MediatR;

namespace Marketplace.Catalog.Api.Application;

public record ProductDto(
    Guid Id,
    string Sku,
    string Title,
    string? Description,
    Guid? CategoryId,
    decimal Price,
    string Currency,
    bool IsActive,
    long? ShopifyProductId,
    string Source);

// --- Commands / Queries ---
public record CreateProductCommand(
    string Sku,
    string Title,
    string? Description,
    Guid? CategoryId,
    decimal Price,
    string Currency) : IRequest<Result<ProductDto>>;

public record GetProductsQuery(int Page = 1, int PageSize = 20) : IRequest<Result<IReadOnlyList<ProductDto>>>;

// --- Validation ---
public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}
