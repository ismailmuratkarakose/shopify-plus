using FluentValidation;
using Marketplace.BuildingBlocks.Results;
using Marketplace.Order.Api.Domain;
using MediatR;

namespace Marketplace.Order.Api.Application;

public record OrderItemDto(Guid ProductId, string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);
public record OrderDto(Guid Id, Guid MerchantId, string? BuyerRef, string Status, string Currency, decimal TotalAmount, string? StatusReason, IReadOnlyList<OrderItemDto> Items);

public record CreateOrderItem(Guid ProductId, string Sku, int Quantity, decimal UnitPrice);
/// <summary>
/// Sipariş SATICI merchant'a aittir. Pazaryerinde alıcı (buyer) satıcıdan farklıdır:
/// MerchantId satıcıyı (order tenant), BuyerRef alıcıyı (JWT sub, endpoint doldurur) belirtir.
/// MerchantId boşsa istek sahibinin tenant'ı kullanılır (merchant kendi adına sipariş).
/// </summary>
public record CreateOrderCommand(string Currency, IReadOnlyList<CreateOrderItem> Items,
    Guid? MerchantId = null, string? BuyerRef = null) : IRequest<Result<OrderDto>>;

public record GetOrdersQuery(int Page = 1, int PageSize = 20) : IRequest<Result<IReadOnlyList<OrderDto>>>;
public record GetOrderByIdQuery(Guid Id) : IRequest<Result<OrderDto>>;

// Alıcı kapsamı: bir müşterinin farklı satıcılardaki tüm siparişleri (tenant filtresi dışı).
public record GetMyPurchasesQuery(string BuyerRef, int Page = 1, int PageSize = 20) : IRequest<Result<IReadOnlyList<OrderDto>>>;
public record GetMyPurchaseByIdQuery(string BuyerRef, Guid Id) : IRequest<Result<OrderDto>>;

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Sipariş en az bir kalem içermeli.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Quantity).GreaterThan(0);
            i.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
            i.RuleFor(x => x.Sku).NotEmpty();
        });
    }
}

public static class OrderMapping
{
    public static OrderDto ToDto(this Domain.Order o) => new(
        o.Id, o.TenantId, o.BuyerRef, o.Status.ToString(), o.Currency, o.TotalAmount, o.StatusReason,
        o.Items.Select(i => new OrderItemDto(i.ProductId, i.Sku, i.Quantity, i.UnitPrice, i.LineTotal)).ToList());
}
