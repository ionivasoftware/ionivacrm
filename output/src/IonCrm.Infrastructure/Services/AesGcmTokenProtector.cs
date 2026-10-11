using System.Security.Cryptography;
using System.Text;
using IonCrm.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// AES-256-GCM protection for platform tokens at rest. Key material: <c>Social:TokenKey</c>
/// (any string, ideally 32+ random bytes base64) — falls back to <c>JwtSettings:Secret</c> so the
/// feature works without extra setup. Output: base64(nonce(12) ‖ tag(16) ‖ ciphertext).
/// WHY NOT ASP.NET Data Protection: its key ring would live on Railway's ephemeral disk and vanish
/// on every deploy, making every stored token unreadable.
/// </summary>
public sealed class AesGcmTokenProtector : ITokenProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmTokenProtector(IConfiguration configuration)
        : this(configuration["Social:TokenKey"] is { Length: > 0 } k ? k : configuration["JwtSettings:Secret"])
    {
    }

    /// <summary>Test / explicit-key constructor.</summary>
    public AesGcmTokenProtector(string? keyMaterial)
    {
        if (string.IsNullOrWhiteSpace(keyMaterial))
            throw new InvalidOperationException("Token şifreleme anahtarı yok: Social:TokenKey ya da JwtSettings:Secret ayarlanmalı.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
    }

    public string Protect(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
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

    public string Unprotect(string ciphertext)
    {
        byte[] data;
        try { data = Convert.FromBase64String(ciphertext); }
        catch (FormatException ex) { throw new CryptographicException("Şifreli token biçimi bozuk.", ex); }
        if (data.Length < NonceSize + TagSize) throw new CryptographicException("Şifreli token çok kısa.");

        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(NonceSize, TagSize);
        var cipher = data.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
