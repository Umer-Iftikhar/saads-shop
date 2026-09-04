using System.ComponentModel.DataAnnotations;

namespace SaadsShop.Api.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Origins allowed to call the API with credentials. Never "*" — a wildcard
    /// and credentials together is rejected by browsers anyway, and quietly
    /// disables the protection CORS exists to provide.
    /// </summary>
    public string[] AllowedOrigins { get; init; } = [];

    /// <summary>Where the browser is sent after Google returns.</summary>
    public string ExternalLoginRedirectUri { get; init; } = "/shop-panel/auth/callback";

    [Range(1, 20)]  public int MaxFailedAccessAttempts { get; init; } = 5;
    [Range(1, 240)] public int LockoutMinutes          { get; init; } = 15;

    /// <summary>Name of the cookie carrying the refresh token. HttpOnly, Secure, SameSite=Strict.</summary>
    public string RefreshCookieName { get; init; } = "saadsshop_rt";

    /// <summary>
    /// Set false only when the API and the SPA are served from different sites
    /// and a cross-site cookie is genuinely required. Strict is the default
    /// because it is the setting that actually stops CSRF.
    /// </summary>
    public bool RefreshCookieSameSiteStrict { get; init; } = true;
}
