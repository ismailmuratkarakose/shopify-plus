namespace Marketplace.Payment.Api.Providers;

public record PaymentCharge(Guid OrderId, decimal Amount, string Currency);
public record PaymentResult(bool Success, string? ProviderPaymentId, string? Error);

/// <summary>
/// Ödeme sağlayıcı soyutlaması. Implementasyonlar: simulator (lokal), iyzico, paypal.
/// Sağlayıcı ve credential merchant başına <see cref="IPaymentProviderResolver"/> ile çözülür.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }
    Task<PaymentResult> ChargeAsync(IReadOnlyDictionary<string, string>? credentials, PaymentCharge charge, CancellationToken ct);
}
