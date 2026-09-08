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
    /// <remarks>
    /// Each entry must be a bare scheme + host + optional port, with no trailing
    /// slash and no path: an <c>Origin</c> header is compared as an exact
    /// string, so "https://saadsshop.pk/" matches nothing and fails as a browser
    /// error nobody can see from the server. <see cref="IsWellFormedOrigin"/>
    /// catches that at startup instead.
    ///
    /// Empty is legitimate and means "same-origin only" — the SPA is served from
    /// the same site, or proxied to it, so no cross-origin request is ever made.
    /// </remarks>
    public string[] AllowedOrigins { get; init; } = [];

    /// <summary>True when <paramref name="origin"/> is shaped like an Origin header.</summary>
    public static bool IsWellFormedOrigin(string origin)
        => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
           && uri.Scheme is "http" or "https"
           && string.IsNullOrEmpty(uri.Query)
           && string.IsNullOrEmpty(uri.Fragment)
           // Uri normalises a bare authority to "/", so anything longer is a
           // path — and a trailing slash in configuration lands here too.
           && uri.AbsolutePath == "/"
           && !origin.EndsWith('/');

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
