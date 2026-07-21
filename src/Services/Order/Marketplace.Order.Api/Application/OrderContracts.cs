using FluentValidation;
using Marketplace.BuildingBlocks.Results;
using Marketplace.Order.Api.Domain;
using MediatR;

namespace Marketplace.Order.Api.Application;

public record OrderItemDto(Guid ProductId, string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);
public record OrderDto(Guid Id, string Status, string Currency, decimal TotalAmount, string? StatusReason, IReadOnlyList<OrderItemDto> Items);

public record CreateOrderItem(Guid ProductId, string Sku, int Quantity, decimal UnitPrice);
public record CreateOrderCommand(string Currency, IReadOnlyList<CreateOrderItem> Items) : IRequest<Result<OrderDto>>;

public record GetOrdersQuery(int Page = 1, int PageSize = 20) : IRequest<Result<IReadOnlyList<OrderDto>>>;
public record GetOrderByIdQuery(Guid Id) : IRequest<Result<OrderDto>>;

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
        o.Id, o.Status.ToString(), o.Currency, o.TotalAmount, o.StatusReason,
        o.Items.Select(i => new OrderItemDto(i.ProductId, i.Sku, i.Quantity, i.UnitPrice, i.LineTotal)).ToList());
}
