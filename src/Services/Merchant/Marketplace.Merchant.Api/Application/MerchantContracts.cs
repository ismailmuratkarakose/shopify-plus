using FluentValidation;
using Marketplace.BuildingBlocks.Messaging;
using Marketplace.BuildingBlocks.Results;
using Marketplace.Merchant.Api.Domain;
using MediatR;

namespace Marketplace.Merchant.Api.Application;

// --- DTO'lar ---
public record MerchantDto(Guid Id, string Name, string Slug, string Status, decimal CommissionRate);

/// <summary>Entegrasyon özeti. Secret değerleri ASLA dönülmez; yalnızca hangi alanların dolu olduğu maskeli gösterilir.</summary>
public record IntegrationDto(string Provider, bool IsActive, IReadOnlyDictionary<string, string> MaskedConfig);

// --- Commands / Queries ---
public record CreateMerchantCommand(Guid? Id, string Name, decimal CommissionRate) : IRequest<Result<MerchantDto>>;
public record GetMerchantsQuery : IRequest<Result<IReadOnlyList<MerchantDto>>>;
public record GetMyMerchantQuery : IRequest<Result<MerchantDto>>;

public record UpsertIntegrationCommand(string Provider, Dictionary<string, string> Config)
    : IRequest<Result<IntegrationDto>>;
public record GetIntegrationsQuery : IRequest<Result<IReadOnlyList<IntegrationDto>>>;

// --- Validation ---
public sealed class CreateMerchantValidator : AbstractValidator<CreateMerchantCommand>
{
    public CreateMerchantValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CommissionRate).InclusiveBetween(0m, 1m);
    }
}

public sealed class UpsertIntegrationValidator : AbstractValidator<UpsertIntegrationCommand>
{
    public UpsertIntegrationValidator()
    {
        RuleFor(x => x.Provider)
            .Must(p => IntegrationProvider.All.Contains(p))
            .WithMessage($"Provider şunlardan biri olmalı: {string.Join(", ", IntegrationProvider.All)}");
        RuleFor(x => x.Config).NotEmpty().WithMessage("Konfigürasyon boş olamaz.");
    }
}

// --- Integration Events (outbox üzerinden) ---
public record MerchantRegisteredIntegrationEvent : IntegrationEvent
{
    public Guid MerchantId { get; init; }
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;
}

/// <summary>Bir merchant Shopify/ödeme sağlayıcı bağladığında yayınlanır. Secret İÇERMEZ.</summary>
public record MerchantIntegrationConfiguredIntegrationEvent : IntegrationEvent
{
    public Guid MerchantId { get; init; }
    public string Provider { get; init; } = default!;
    public bool IsActive { get; init; }
}
