namespace Marketplace.BuildingBlocks.Messaging;

/// <summary>
/// Servisler arası event bus üzerinden yayınlanan olayların tabanı.
/// Outbox pattern ile atomik olarak DB transaction'ı içinde kaydedilir, sonra RabbitMQ'ya publish edilir.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Olayı üreten merchant. Tüketici tarafında tenant korelasyonu için.</summary>
    public Guid? TenantId { get; init; }
}
