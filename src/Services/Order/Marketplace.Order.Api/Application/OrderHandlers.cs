using FluentValidation;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.BuildingBlocks.Results;
using Marketplace.Contracts;
using Marketplace.Order.Api.Domain;
using Marketplace.Order.Api.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Order.Api.Application;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<OrderDto>>
{
    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IValidator<CreateOrderCommand> _validator;

    public CreateOrderHandler(OrderDbContext db, ITenantContext tenant, IValidator<CreateOrderCommand> validator)
    {
        _db = db;
        _tenant = tenant;
        _validator = validator;
    }

    public async Task<Result<OrderDto>> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Result.Failure<OrderDto>(Error.Validation("order.invalid", validation.Errors[0].ErrorMessage));

        // Sipariş SATICI merchant'a ait: MerchantId (BFF checkout böler) yoksa istek sahibinin tenant'ı.
        var sellerTenant = request.MerchantId ?? _tenant.TenantId;
        if (sellerTenant is null)
            return Result.Failure<OrderDto>(Error.Unauthorized("tenant.missing", "Siparişin satıcı merchant'ı belirsiz."));

        var order = new Domain.Order
        {
            TenantId = sellerTenant.Value,   // otomatik atamayı ez: sahip satıcıdır
            BuyerRef = request.BuyerRef,
            Status = OrderStatus.Pending,
            Currency = request.Currency,
            Items = request.Items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                Sku = i.Sku,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);

        _db.Orders.Add(order);

        // Stok rezervasyonunu tetikle: OrderPlaced → Inventory tüketir (satıcı tenant'ında).
        _db.EnqueueIntegrationEvent(new OrderPlacedIntegrationEvent
        {
            TenantId = sellerTenant,
            OrderId = order.Id,
            Total = order.TotalAmount,
            Currency = order.Currency,
            Lines = order.Items.Select(i => new OrderLine
            {
                ProductId = i.ProductId,
                Sku = i.Sku,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        });

        await _db.SaveChangesAsync(ct);
        return Result.Success(order.ToDto());
    }
}

public sealed class GetOrdersHandler : IRequestHandler<GetOrdersQuery, Result<IReadOnlyList<OrderDto>>>
{
    private readonly OrderDbContext _db;
    public GetOrdersHandler(OrderDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<OrderDto>>> Handle(GetOrdersQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var orders = await _db.Orders
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<OrderDto>>(orders.Select(o => o.ToDto()).ToList());
    }
}

public sealed class GetOrderByIdHandler : IRequestHandler<GetOrderByIdQuery, Result<OrderDto>>
{
    private readonly OrderDbContext _db;
    public GetOrderByIdHandler(OrderDbContext db) => _db = db;

    public async Task<Result<OrderDto>> Handle(GetOrderByIdQuery request, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == request.Id, ct);
        return order is null
            ? Result.Failure<OrderDto>(Error.NotFound("order.not_found", "Sipariş bulunamadı."))
            : Result.Success(order.ToDto());
    }
}

/// <summary>Alıcı kapsamı: müşterinin farklı satıcılardaki tüm siparişleri (tenant filtresi dışı, BuyerRef ile).</summary>
public sealed class GetMyPurchasesHandler : IRequestHandler<GetMyPurchasesQuery, Result<IReadOnlyList<OrderDto>>>
{
    private readonly OrderDbContext _db;
    public GetMyPurchasesHandler(OrderDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<OrderDto>>> Handle(GetMyPurchasesQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var orders = await _db.Orders
            .IgnoreQueryFilters()
            .Include(o => o.Items)
            .Where(o => o.BuyerRef == request.BuyerRef)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<OrderDto>>(orders.Select(o => o.ToDto()).ToList());
    }
}

public sealed class GetMyPurchaseByIdHandler : IRequestHandler<GetMyPurchaseByIdQuery, Result<OrderDto>>
{
    private readonly OrderDbContext _db;
    public GetMyPurchaseByIdHandler(OrderDbContext db) => _db = db;

    public async Task<Result<OrderDto>> Handle(GetMyPurchaseByIdQuery request, CancellationToken ct)
    {
        var order = await _db.Orders
            .IgnoreQueryFilters()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == request.Id && o.BuyerRef == request.BuyerRef, ct);
        return order is null
            ? Result.Failure<OrderDto>(Error.NotFound("order.not_found", "Sipariş bulunamadı."))
            : Result.Success(order.ToDto());
    }
}
