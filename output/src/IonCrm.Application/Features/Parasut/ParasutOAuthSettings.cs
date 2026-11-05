namespace IonCrm.Application.Features.Parasut;

/// <summary>
/// Server configuration of Paraşüt's NEW authorisation model (authorization code + PKCE on Keycloak). Pure — unit-tested.
/// Paraşüt's 04–05.11.2026 announcement removes applications' access to the user's password; connecting now happens on
/// Paraşüt's own login screen. The legacy password flow keeps its per-row credentials untouched until cut-over.
/// <list type="bullet">
/// <item><c>Parasut__OAuth__Environment</c> — <c>Staging</c> or <c>Production</c> (default Production). Picks BOTH the identity
/// and the REST base together, so a mismatch is impossible (staging REST lives on a different host).</item>
/// <item><c>Parasut__OAuth__ClientId</c> / <c>Parasut__OAuth__ClientSecret</c> — DevHub ▸ API Bilgilerim. The secret is required
/// for a confidential client and left empty for a public one (PKCE only) — it is then never sent.</item>
/// <item><c>Parasut__OAuth__RedirectUri</c> — the callback address registered in DevHub, byte-for-byte. In production at most
/// 3 addresses are declared at application time and CANNOT be changed afterwards.</item>
/// </list>
/// Endpoints are Keycloak's, not the retired <c>api.parasut.com/oauth/*</c>.
/// </summary>
public sealed class ParasutOAuthSettings
{
    /// <summary>Path of the callback endpoint — the tail of the address registered with Paraşüt.</summary>
    public const string CallbackPath = "/api/v1/parasut/callback";

    /// <summary>How long a started authorisation (state + code verifier) stays valid.</summary>
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private const string StagingIdentity = "https://id-staging.parasut.com/realms/parasut/protocol/openid-connect";
    private const string ProductionIdentity = "https://id.parasut.com/realms/parasut/protocol/openid-connect";
    private const string StagingApi = "https://api.heroku-staging.parasut.com";
    private const string ProductionApi = "https://api.parasut.com";

    private readonly Func<string, string?> _config;

    public ParasutOAuthSettings(Func<string, string?> config) => _config = config;

    public bool IsStaging => string.Equals(Clean(_config("Parasut:OAuth:Environment")), "Staging", StringComparison.OrdinalIgnoreCase);

    public string? ClientId => Clean(_config("Parasut:OAuth:ClientId"));

    /// <summary>Null for a public client (PKCE only) — then no <c>client_secret</c> field is sent at all.</summary>
    public string? ClientSecret => Clean(_config("Parasut:OAuth:ClientSecret"));

    public string? RedirectUri => Clean(_config("Parasut:OAuth:RedirectUri"));

    /// <summary>The new flow can be offered: client id + redirect URI are configured.</summary>
    public bool Available => ClientId is not null && RedirectUri is not null;

    /// <summary>REST base without trailing slash — a different host on staging.</summary>
    public string ApiBaseUrl => IsStaging ? StagingApi : ProductionApi;

    private string IdentityBase => IsStaging ? StagingIdentity : ProductionIdentity;

    public string AuthorizationEndpoint => $"{IdentityBase}/auth";
    public string TokenEndpoint => $"{IdentityBase}/token";
    public string RevocationEndpoint => $"{IdentityBase}/revoke";

    /// <summary>Values pasted from a clipboard may carry whitespace or line breaks; a control character inside invalidates the value.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 || trimmed.Any(char.IsControl) ? null : trimmed;
    }
}
