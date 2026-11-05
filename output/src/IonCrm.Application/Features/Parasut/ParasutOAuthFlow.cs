using System.Security.Cryptography;
using System.Text;
using IonCrm.Application.Common.Interfaces;

namespace IonCrm.Application.Features.Parasut;

/// <summary>Pure building blocks of the authorization-code + PKCE flow: random state, verifier/challenge, state hashing, secrets at rest.</summary>
public static class ParasutOAuthFlow
{
    public const string OutcomeConnected = "connected";
    public const string OutcomeError = "error";

    // Reason codes carried back to the settings page (?parasut=error&reason=…) — codes, not UI text.
    public const string ReasonState = "state";
    public const string ReasonExpired = "expired";
    public const string ReasonDenied = "denied";
    public const string ReasonParasut = "parasut";
    public const string ReasonCode = "code";
    public const string ReasonVerifier = "verifier";
    public const string ReasonExchange = "exchange";
    public const string ReasonCompany = "company";

    /// <summary>32 random bytes, base64url — single use, only its hash is stored.</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>PKCE verifier: 64 random bytes → 86 characters (RFC 7636 allows 43–128).</summary>
    public static string NewCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));

    /// <summary>S256 challenge = BASE64URL(SHA256(ASCII(verifier))).</summary>
    public static string CodeChallenge(string codeVerifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    /// <summary>The state itself is never stored — only its SHA-256 (hex, 64 chars).</summary>
    public static string HashState(string state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Encrypts a secret for the row; empty stays empty.</summary>
    public static string? ProtectSecret(ITokenProtector protector, string? value) =>
        string.IsNullOrEmpty(value) ? value : protector.Protect(value);

    /// <summary>
    /// Decrypts a stored secret. A value that does not decrypt is returned AS IS: live rows written before encryption hold the
    /// plain refresh token, must keep working without a data migration, and encrypt themselves on the next refresh.
    /// </summary>
    public static string ReadSecret(ITokenProtector protector, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        try { return protector.Unprotect(stored); }
        catch (CryptographicException) { return stored; }
        catch (FormatException) { return stored; }
    }
}

/// <summary>
/// The token endpoint rejected the request (HTTP 400/401/403 — <c>invalid_grant</c> and friends). On the new flow this means the
/// connection is dead and a human must re-authorise; a network error or 5xx is NOT this exception and is retried later.
/// </summary>
public sealed class ParasutTokenRejectedException : Exception
{
    public int StatusCode { get; }

    public ParasutTokenRejectedException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}
