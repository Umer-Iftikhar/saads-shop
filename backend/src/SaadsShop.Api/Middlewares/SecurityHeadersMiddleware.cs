namespace SaadsShop.Api.Middlewares;

/// <summary>
/// Response headers that cost nothing and close off whole classes of attack.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // The API returns JSON, never markup, so it can be maximally strict:
        // nothing may be loaded, and nothing may frame it.
        headers["Content-Security-Policy"]  = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        headers["X-Content-Type-Options"]   = "nosniff";
        headers["X-Frame-Options"]          = "DENY";
        headers["Referrer-Policy"]          = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"]       = "geolocation=(), camera=(), microphone=(), payment=()";

        // Free reconnaissance otherwise — the version tells an attacker which
        // advisories to try.
        headers.Remove("Server");
        headers.Remove("X-Powered-By");
        headers.Remove("X-AspNet-Version");

        await next(context);
    }
}
