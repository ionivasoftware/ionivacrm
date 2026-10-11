using System.Security.Cryptography;
using System.Text;

namespace IonCrm.Application.Common.Helpers;

/// <summary>OAuth 2.0 PKCE (RFC 7636) and opaque-state helpers for the social-account connect flows.</summary>
public static class Pkce
{
    /// <summary>43–128 char URL-safe verifier (here 64 random bytes → 86 chars).</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Unguessable state token for the OAuth round trip.</summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
