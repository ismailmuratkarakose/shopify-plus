using Marketplace.ShopifySync.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Shopify;

/// <summary>
/// Periyodik mutabakat: bağlı tüm mağazaları düzenli aralıklarla yeniden senkronlar.
/// Amaç, kaçan/gecikmiş webhook'lar nedeniyle oluşabilecek veri farklarını (drift) kapatmaktır.
/// Yapılandırma: Sync:Reconciliation:{Enabled, IntervalMinutes, InitialDelaySeconds}
/// </summary>
public sealed class ReconciliationService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _config;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(IServiceProvider sp, IConfiguration config, ILogger<ReconciliationService> logger)
    {
        _sp = sp;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue<bool?>("Sync:Reconciliation:Enabled") ?? true;
        if (!enabled)
        {
            _logger.LogInformation("Periyodik mutabakat kapalı (Sync:Reconciliation:Enabled=false).");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _config.GetValue<int?>("Sync:Reconciliation:IntervalMinutes") ?? 60));
        var initialDelay = TimeSpan.FromSeconds(Math.Max(5, _config.GetValue<int?>("Sync:Reconciliation:InitialDelaySeconds") ?? 60));

        _logger.LogInformation("Periyodik mutabakat aktif: ilk çalışma {Delay}, aralık {Interval}.", initialDelay, interval);

        try
        {
            await Task.Delay(initialDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Uygulama kapanıyor — normal.
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> storeIds;
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopifySyncDbContext>();
            // Arka plan işi HTTP kapsamı taşımaz → tüm mağazalar için kiracı filtresini atla.
            storeIds = await db.Integrations.IgnoreQueryFilters()
                .Where(i => i.IsActive)
                .Select(i => i.StoreId)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mutabakat: bağlı mağazalar listelenemedi.");
            return;
        }

        if (storeIds.Count == 0) return;

        foreach (var storeId in storeIds)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                // Her mağaza için ayrı kapsam: kiracı bağlamı ve DbContext izole kalsın.
                using var scope = _sp.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<StoreSyncService>();
                await sync.SyncAsync(storeId, "reconciliation", ct);
            }
            catch (Exception ex)
            {
                // Bir mağazanın hatası diğerlerini durdurmasın (hata durumu kaydına yazıldı).
                _logger.LogWarning(ex, "Mutabakat başarısız: merchant={MerchantId}", storeId);
            }
        }
    }
}
