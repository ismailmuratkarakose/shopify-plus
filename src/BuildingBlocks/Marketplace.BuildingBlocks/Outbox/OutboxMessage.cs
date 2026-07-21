namespace Marketplace.BuildingBlocks.Outbox;

/// <summary>
/// Transactional outbox kaydı. İş verisiyle aynı DB transaction'ında yazılır;
/// arka plandaki dispatcher güvenilir şekilde event bus'a (RabbitMQ) yayınlar.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Olay CLR tipinin AssemblyQualifiedName'i (deserialize + doğru mesaj tipiyle publish için).</summary>
    public string Type { get; set; } = default!;

    /// <summary>JSON serialize edilmiş olay gövdesi.</summary>
    public string Payload { get; set; } = default!;

    public DateTimeOffset OccurredOn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedOn { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
