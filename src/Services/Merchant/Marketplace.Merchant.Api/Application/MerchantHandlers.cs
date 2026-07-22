using System.Text.Json;
using FluentValidation;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.BuildingBlocks.Results;
using Marketplace.BuildingBlocks.Security;
using Marketplace.Contracts;
using Marketplace.Merchant.Api.Domain;
using Marketplace.Merchant.Api.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Merchant.Api.Application;

public sealed class CreateMerchantHandler : IRequestHandler<CreateMerchantCommand, Result<MerchantDto>>
{
    private readonly MerchantDbContext _db;
    private readonly IValidator<CreateMerchantCommand> _validator;

    public CreateMerchantHandler(MerchantDbContext db, IValidator<CreateMerchantCommand> validator)
    {
        _db = db;
        _validator = validator;
    }

    public async Task<Result<MerchantDto>> Handle(CreateMerchantCommand request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Result.Failure<MerchantDto>(Error.Validation("merchant.invalid", validation.Errors[0].ErrorMessage));

        var slug = Slugify(request.Name);
        // Platform kapsamında sorgu (owner). Slug benzersizliği:
        if (await _db.Merchants.IgnoreQueryFilters().AnyAsync(m => m.Slug == slug, ct))
            return Result.Failure<MerchantDto>(Error.Conflict("merchant.slug_exists", $"'{slug}' slug zaten mevcut."));

        var merchant = new Domain.Merchant
        {
            Id = request.Id ?? Guid.NewGuid(),
            Name = request.Name,
            Slug = slug,
            Status = MerchantStatus.Active,
            CommissionRate = request.CommissionRate
        };

        _db.Merchants.Add(merchant);
        _db.EnqueueIntegrationEvent(new MerchantRegisteredIntegrationEvent
        {
            TenantId = merchant.Id,
            MerchantId = merchant.Id,
            Name = merchant.Name,
            Slug = merchant.Slug,
            CommissionRate = merchant.CommissionRate
        });
        await _db.SaveChangesAsync(ct);

        return Result.Success(Map(merchant));
    }

    private static string Slugify(string name)
    {
        var slug = new string(name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    internal static MerchantDto Map(Domain.Merchant m) =>
        new(m.Id, m.Name, m.Slug, m.Status.ToString(), m.CommissionRate);
}

public sealed class GetMerchantsHandler : IRequestHandler<GetMerchantsQuery, Result<IReadOnlyList<MerchantDto>>>
{
    private readonly MerchantDbContext _db;
    public GetMerchantsHandler(MerchantDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<MerchantDto>>> Handle(GetMerchantsQuery request, CancellationToken ct)
    {
        var items = await _db.Merchants
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new MerchantDto(m.Id, m.Name, m.Slug, m.Status.ToString(), m.CommissionRate))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<MerchantDto>>(items);
    }
}

public sealed class GetMyMerchantHandler : IRequestHandler<GetMyMerchantQuery, Result<MerchantDto>>
{
    private readonly MerchantDbContext _db;
    private readonly ITenantContext _tenant;

    public GetMyMerchantHandler(MerchantDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Result<MerchantDto>> Handle(GetMyMerchantQuery request, CancellationToken ct)
    {
        if (_tenant.TenantId is null)
            return Result.Failure<MerchantDto>(Error.Unauthorized("tenant.missing", "İstek bir merchant kapsamı taşımıyor."));

        // Query filter zaten kendi merchant'ına sınırlar.
        var m = await _db.Merchants.FirstOrDefaultAsync(ct);
        return m is null
            ? Result.Failure<MerchantDto>(Error.NotFound("merchant.not_found", "Merchant kaydı bulunamadı."))
            : Result.Success(CreateMerchantHandler.Map(m));
    }
}

public sealed class UpsertIntegrationHandler : IRequestHandler<UpsertIntegrationCommand, Result<IntegrationDto>>
{
    private readonly MerchantDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _protector;
    private readonly IValidator<UpsertIntegrationCommand> _validator;

    public UpsertIntegrationHandler(
        MerchantDbContext db,
        ITenantContext tenant,
        ISecretProtector protector,
        IValidator<UpsertIntegrationCommand> validator)
    {
        _db = db;
        _tenant = tenant;
        _protector = protector;
        _validator = validator;
    }

    public async Task<Result<IntegrationDto>> Handle(UpsertIntegrationCommand request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Result.Failure<IntegrationDto>(Error.Validation("integration.invalid", validation.Errors[0].ErrorMessage));

        if (_tenant.TenantId is null)
            return Result.Failure<IntegrationDto>(Error.Unauthorized("tenant.missing", "İstek bir merchant kapsamı taşımıyor."));

        var encrypted = _protector.Protect(JsonSerializer.Serialize(request.Config));

        var existing = await _db.Integrations.FirstOrDefaultAsync(i => i.Provider == request.Provider, ct);
        if (existing is null)
        {
            existing = new MerchantIntegration { Provider = request.Provider, EncryptedConfig = encrypted, IsActive = true };
            _db.Integrations.Add(existing);
        }
        else
        {
            existing.EncryptedConfig = encrypted;
            existing.IsActive = true;
        }

        _db.EnqueueIntegrationEvent(new MerchantIntegrationConfiguredIntegrationEvent
        {
            TenantId = _tenant.TenantId,
            MerchantId = _tenant.TenantId.Value,
            Provider = request.Provider,
            IsActive = true
        });
        await _db.SaveChangesAsync(ct);

        return Result.Success(new IntegrationDto(request.Provider, existing.IsActive, Mask(request.Config)));
    }

    internal static IReadOnlyDictionary<string, string> Mask(Dictionary<string, string> config) =>
        config.ToDictionary(kv => kv.Key, kv => MaskValue(kv.Value));

    private static string MaskValue(string v) =>
        string.IsNullOrEmpty(v) ? "" : v.Length <= 4 ? "****" : $"****{v[^4..]}";
}

public sealed class GetIntegrationsHandler : IRequestHandler<GetIntegrationsQuery, Result<IReadOnlyList<IntegrationDto>>>
{
    private readonly MerchantDbContext _db;
    private readonly ISecretProtector _protector;

    public GetIntegrationsHandler(MerchantDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<Result<IReadOnlyList<IntegrationDto>>> Handle(GetIntegrationsQuery request, CancellationToken ct)
    {
        var rows = await _db.Integrations.ToListAsync(ct);
        var result = rows.Select(i =>
        {
            var config = JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Unprotect(i.EncryptedConfig))
                         ?? new();
            return new IntegrationDto(i.Provider, i.IsActive, UpsertIntegrationHandler.Mask(config));
        }).ToList();
        return Result.Success<IReadOnlyList<IntegrationDto>>(result);
    }
}
