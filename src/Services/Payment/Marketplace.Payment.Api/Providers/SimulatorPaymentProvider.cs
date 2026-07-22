using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Marketplace.Payment.Api.Providers;

/// <summary>
/// Gerçek ödeme sağlayıcı olmadan lokal test için. Varsayılan başarılı;
/// tutar config'teki FailAmount'a eşitse başarısız döner (telafi akışını test etmek için).
/// </summary>
public sealed class SimulatorPaymentProvider : IPaymentProvider
{
    private readonly decimal _failAmount;
    private readonly ILogger<SimulatorPaymentProvider> _logger;

    public string Name => "simulator";

    public SimulatorPaymentProvider(IConfiguration config, ILogger<SimulatorPaymentProvider> logger)
    {
        _failAmount = decimal.TryParse(config["Payment:Simulator:FailAmount"], NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : 66.66m;
        _logger = logger;
    }

    public Task<PaymentResult> ChargeAsync(IReadOnlyDictionary<string, string>? credentials, PaymentCharge charge, CancellationToken ct)
    {
        if (charge.Amount == _failAmount)
        {
            _logger.LogInformation("[SIMULATOR] Ödeme REDDEDİLDİ (test): order={Order} amount={Amount}", charge.OrderId, charge.Amount);
            return Task.FromResult(new PaymentResult(false, null, "Simülatör: test amacıyla reddedildi."));
        }

        var id = $"sim_{Guid.NewGuid():N}";
        _logger.LogInformation("[SIMULATOR] Ödeme BAŞARILI: order={Order} amount={Amount} {Cur} -> {Id}",
            charge.OrderId, charge.Amount, charge.Currency, id);
        return Task.FromResult(new PaymentResult(true, id, null));
    }
}
