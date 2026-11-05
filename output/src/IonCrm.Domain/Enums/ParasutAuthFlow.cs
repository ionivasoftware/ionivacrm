namespace IonCrm.Domain.Enums;

/// <summary>
/// How a <c>ParasutConnection</c> was authorised — decides the refresh path.
/// Paraşüt's 04–05.11.2026 announcement retires the password grant (~05.12.2026); until then both flows live side by side.
/// </summary>
public enum ParasutAuthFlow
{
    /// <summary>Legacy: stored Paraşüt e-mail + password, <c>grant_type=password</c> on api.parasut.com. Existing rows.</summary>
    Password = 1,

    /// <summary>New: authorization code + PKCE on Keycloak (id.parasut.com). No password stored; refresh token is the only key.</summary>
    OAuth2 = 2,
}
