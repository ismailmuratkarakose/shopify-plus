using System.Security.Cryptography;
using System.Text;

namespace Marketplace.BuildingBlocks.Security;

/// <summary>
/// Merchant ödeme/Shopify anahtarları gibi hassas değerlerin at-rest şifrelenmesi.
/// Anahtar konfigürasyondan (K8s secret) gelir.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

/// <summary>
/// AES-256-GCM. Çıktı formatı: base64(nonce(12) | tag(16) | ciphertext).
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmSecretProtector(string base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new ArgumentException("Şifreleme anahtarı yapılandırılmamış.", nameof(base64Key));

        _key = Convert.FromBase64String(base64Key);
        if (_key.Length is not (16 or 24 or 32))
            throw new ArgumentException("Anahtar 128/192/256-bit (base64) olmalı.", nameof(base64Key));
    }

    public string Protect(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(output);
    }

    public string Unprotect(string protectedValue)
    {
        var data = Convert.FromBase64String(protectedValue);
        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(NonceSize, TagSize);
        var cipher = data.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
