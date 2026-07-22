namespace Marketplace.Payment.Api.Providers;

/// <summary>Bir merchant için kullanılacak ödeme sağlayıcıyı ve credential'ını çözer.</summary>
public interface IPaymentProviderResolver
{
    Task<(IPaymentProvider Provider, IReadOnlyDictionary<string, string>? Credentials)> ResolveAsync(Guid tenantId, CancellationToken ct);
}

public sealed class PaymentProviderResolver : IPaymentProviderResolver
{
    private static readonly string[] LiveProviders = ["iyzico", "paypal"];

    private readonly string _mode;
    private readonly SimulatorPaymentProvider _simulator;
    private readonly IyzicoPaymentProvider _iyzico;
    private readonly PayPalPaymentProvider _paypal;
    private readonly IMerchantCredentialClient _merchant;

    public PaymentProviderResolver(
        IConfiguration config,
        SimulatorPaymentProvider simulator,
        IyzicoPaymentProvider iyzico,
        PayPalPaymentProvider paypal,
        IMerchantCredentialClient merchant)
    {
        _mode = config["Payment:Mode"] ?? "simulator";
        _simulator = simulator;
        _iyzico = iyzico;
        _paypal = paypal;
        _merchant = merchant;
    }

    public async Task<(IPaymentProvider, IReadOnlyDictionary<string, string>?)> ResolveAsync(Guid tenantId, CancellationToken ct)
    {
        if (!string.Equals(_mode, "live", StringComparison.OrdinalIgnoreCase))
            return (_simulator, null);

        // Live: merchant'ın yapılandırdığı ilk aktif sağlayıcıyı kullan.
        foreach (var name in LiveProviders)
        {
            var creds = await _merchant.GetIntegrationConfigAsync(tenantId, name, ct);
            if (creds is not null)
                return name == "iyzico" ? (_iyzico, creds) : (_paypal, creds);
        }

        // Sağlayıcı yoksa simülatöre düş (dev güvenliği).
        return (_simulator, null);
    }
}
