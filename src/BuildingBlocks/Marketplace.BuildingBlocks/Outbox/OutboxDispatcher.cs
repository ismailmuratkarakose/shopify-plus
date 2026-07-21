using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marketplace.BuildingBlocks.Outbox;

public sealed class OutboxOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 20;
    public int MaxRetries { get; set; } = 10;
}

/// <summary>
/// İşlenmemiş outbox kayıtlarını periyodik tarayıp event bus'a yayınlayan arka plan servisi.
/// En-az-bir-kez teslim; tüketiciler idempotent olmalıdır.
/// </summary>
public sealed class OutboxDispatcher<TContext> : BackgroundService where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcher<TContext>> _logger;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        OutboxOptions options,
        ILogger<OutboxDispatcher<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatch döngüsünde hata.");
            }

            await Task.Delay(_options.PollInterval, stoppingToken);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var pending = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedOn == null && m.RetryCount < _options.MaxRetries)
            .OrderBy(m => m.OccurredOn)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        foreach (var message in pending)
        {
            try
            {
                var type = Type.GetType(message.Type)
                    ?? throw new InvalidOperationException($"Tip çözülemedi: {message.Type}");
                var @event = JsonSerializer.Deserialize(message.Payload, type)
                    ?? throw new InvalidOperationException("Payload deserialize edilemedi.");

                await publishEndpoint.Publish(@event, type, ct);
                message.ProcessedOn = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.LastError = ex.Message;
                _logger.LogWarning(ex, "Outbox mesajı yayınlanamadı (Id={Id}, deneme={Retry}).",
                    message.Id, message.RetryCount);
            }
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}

public static class OutboxServiceCollectionExtensions
{
    /// <summary>Belirtilen DbContext için outbox dispatcher'ını kaydeder.</summary>
    public static IServiceCollection AddOutboxDispatcher<TContext>(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null) where TContext : DbContext
    {
        var options = new OutboxOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddHostedService<OutboxDispatcher<TContext>>();
        return services;
    }
}
