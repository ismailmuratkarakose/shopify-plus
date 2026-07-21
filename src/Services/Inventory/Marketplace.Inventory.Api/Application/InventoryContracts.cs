using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Results;
using Marketplace.Inventory.Api.Domain;
using Marketplace.Inventory.Api.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Inventory.Api.Application;

public record InventoryItemDto(Guid ProductId, string Sku, int QuantityOnHand, int QuantityReserved, int Available);

public record GetInventoryQuery : IRequest<Result<IReadOnlyList<InventoryItemDto>>>;
public record AdjustStockCommand(Guid ProductId, int QuantityOnHand) : IRequest<Result<InventoryItemDto>>;

public sealed class GetInventoryHandler : IRequestHandler<GetInventoryQuery, Result<IReadOnlyList<InventoryItemDto>>>
{
    private readonly InventoryDbContext _db;
    public GetInventoryHandler(InventoryDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<InventoryItemDto>>> Handle(GetInventoryQuery request, CancellationToken ct)
    {
        var items = await _db.Items
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InventoryItemDto(i.ProductId, i.Sku, i.QuantityOnHand, i.QuantityReserved,
                i.QuantityOnHand - i.QuantityReserved))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<InventoryItemDto>>(items);
    }
}

public sealed class AdjustStockHandler : IRequestHandler<AdjustStockCommand, Result<InventoryItemDto>>
{
    private readonly InventoryDbContext _db;
    private readonly ITenantContext _tenant;

    public AdjustStockHandler(InventoryDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Result<InventoryItemDto>> Handle(AdjustStockCommand request, CancellationToken ct)
    {
        if (_tenant.TenantId is null)
            return Result.Failure<InventoryItemDto>(Error.Unauthorized("tenant.missing", "İstek bir merchant kapsamı taşımıyor."));
        if (request.QuantityOnHand < 0)
            return Result.Failure<InventoryItemDto>(Error.Validation("stock.negative", "Stok negatif olamaz."));

        var item = await _db.Items.FirstOrDefaultAsync(i => i.ProductId == request.ProductId, ct);
        if (item is null)
        {
            // Ürün henüz Catalog event'iyle gelmemişse elle oluştur.
            item = new InventoryItem { ProductId = request.ProductId, Sku = request.ProductId.ToString(), QuantityOnHand = request.QuantityOnHand };
            _db.Items.Add(item);
        }
        else
        {
            item.QuantityOnHand = request.QuantityOnHand;
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success(new InventoryItemDto(item.ProductId, item.Sku, item.QuantityOnHand, item.QuantityReserved, item.Available));
    }
}
