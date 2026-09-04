using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SaadsShop.Api.Configuration;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(
    IAuthService auth,
    IOptions<AuthOptions> authOptions,
    IOptions<GoogleAuthOptions> googleOptions,
    ILogger<AuthController> logger) : ApiControllerBase
{
    private readonly AuthOptions _auth = authOptions.Value;

    /// <summary>
    /// Step one. Returns a short-lived MFA token, never an access token —
    /// every shop account carries two-factor.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        => FromResult(await auth.LoginAsync(request, CallerIp, ct));

    /// <summary>Step two. On success the session begins and the refresh cookie is set.</summary>
    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.TwoFactor)]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] TwoFactorRequest request, CancellationToken ct)
    {
        var result = await auth.VerifyTwoFactorAsync(request, CallerIp, ct);

        if (!result.IsSuccess) return Problem(result);

        SetRefreshCookie(result.Value!.RefreshToken, result.Value.RefreshTokenExpiresAt);
        return Ok(result.Value.Auth);
    }

    /// <summary>
    /// Rotates the session. The refresh token comes from the HttpOnly cookie,
    /// never from the body — a token JavaScript can read is a token XSS can steal.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var presented = Request.Cookies[_auth.RefreshCookieName];

        if (string.IsNullOrWhiteSpace(presented))
            return Unauthorized(new { title = "Please sign in again." });

        var result = await auth.RefreshAsync(presented, CallerIp, ct);

        if (!result.IsSuccess)
        {
            // Clear the cookie on failure. Leaving a dead token in the browser
            // means every subsequent page load retries and fails.
            DeleteRefreshCookie();
            return Problem(result);
        }

        SetRefreshCookie(result.Value!.RefreshToken, result.Value.RefreshTokenExpiresAt);
        return Ok(result.Value.Auth);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var presented = Request.Cookies[_auth.RefreshCookieName];
        var result    = await auth.LogoutAsync(presented, CurrentUserId ?? string.Empty, ct);

        DeleteRefreshCookie();
        return FromResult(result, StatusCodes.Status204NoContent);
    }

    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.StaffOnly)]
    public async Task<IActionResult> Me(CancellationToken ct)
        => FromResult(await auth.GetCurrentUserAsync(CurrentUserId!, ct));

    // ── two-factor enrolment ─────────────────────────────────────────────────

    [HttpPost("2fa/enroll")]
    [Authorize]
    public async Task<IActionResult> BeginEnrolment(CancellationToken ct)
        => FromResult(await auth.BeginTwoFactorEnrolmentAsync(CurrentUserId!, ct));

    [HttpPost("2fa/confirm")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.TwoFactor)]
    public async Task<IActionResult> ConfirmEnrolment([FromBody] ConfirmTwoFactorRequest request, CancellationToken ct)
        => FromResult(await auth.ConfirmTwoFactorEnrolmentAsync(CurrentUserId!, request, ct));

    // ── Google ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the Google authorization-code flow. The handler adds PKCE and a
    /// state parameter bound to the session for CSRF protection.
    /// </summary>
    [HttpGet("google")]
    [AllowAnonymous]
    public IActionResult GoogleChallenge([FromQuery] string? returnUrl)
    {
        if (!googleOptions.Value.IsConfigured)
            return NotFound(new { title = "Google sign-in is not set up for this shop." });

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleCallback), new { returnUrl })
        };

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("google/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleCallback([FromQuery] string? returnUrl, CancellationToken ct)
    {
        var authenticate = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);

        if (!authenticate.Succeeded || authenticate.Principal is null)
        {
            logger.LogWarning("Google callback failed: {Failure}", authenticate.Failure?.Message);
            return Unauthorized(new { title = "Google sign-in did not complete." });
        }

        var principal   = authenticate.Principal;
        var providerKey = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var email       = principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var displayName = principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

        // Google sends email_verified as a string claim. Absent counts as not
        // verified — failing closed is the only safe reading.
        var emailVerified = string.Equals(
            principal.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        if (providerKey is null || email is null)
            return Unauthorized(new { title = "Google did not return enough information to sign you in." });

        var result = await auth.ExternalLoginAsync(
            GoogleDefaults.AuthenticationScheme, providerKey, email, emailVerified, displayName, CallerIp, ct);

        // The external cookie has done its job; leaving it set would be a second
        // credential lying around for no reason.
        await HttpContext.SignOutAsync(GoogleDefaults.AuthenticationScheme);

        if (!result.IsSuccess) return Problem(result);

        // Google proved the identity; 2FA still has to prove possession. Hand
        // the challenge token to the SPA rather than completing the sign-in.
        var target = string.IsNullOrWhiteSpace(returnUrl) ? _auth.ExternalLoginRedirectUri : returnUrl;
        var separator = target.Contains('?') ? '&' : '?';

        return Redirect($"{target}{separator}mfaToken={Uri.EscapeDataString(result.Value!.MfaToken)}" +
                        $"&enrolled={result.Value.IsTwoFactorEnrolled.ToString().ToLowerInvariant()}");
    }

    // ── staff accounts ───────────────────────────────────────────────────────

    [HttpGet("staff")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    public async Task<IActionResult> GetStaff(CancellationToken ct)
        => FromResult(await auth.GetStaffAsync(ct));

    [HttpPost("staff")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    public async Task<IActionResult> CreateStaff([FromBody] CreateStaffRequest request, CancellationToken ct)
        => FromResult(await auth.CreateStaffAsync(request, ct), StatusCodes.Status201Created);

    [HttpPost("staff/role")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    public async Task<IActionResult> SetRole([FromBody] SetRoleRequest request, CancellationToken ct)
        => FromResult(await auth.SetRoleAsync(request, ct), StatusCodes.Status204NoContent);

    // ── cookie helpers ───────────────────────────────────────────────────────

    private void SetRefreshCookie(string token, DateTime expiresAt)
        => Response.Cookies.Append(_auth.RefreshCookieName, token, new CookieOptions
        {
            // Out of JavaScript's reach, so XSS cannot read it.
            HttpOnly = true,

            // Never sent over plain HTTP.
            Secure   = true,

            // Strict is what actually stops CSRF; it is relaxed only when the
            // SPA genuinely lives on another site.
            SameSite = _auth.RefreshCookieSameSiteStrict ? SameSiteMode.Strict : SameSiteMode.None,

            // Scoped to the refresh and logout endpoints, so it is not attached
            // to every ordinary API call.
            Path     = "/api/auth",
            Expires  = expiresAt
        });

    private void DeleteRefreshCookie()
        => Response.Cookies.Append(_auth.RefreshCookieName, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure   = true,
            SameSite = _auth.RefreshCookieSameSiteStrict ? SameSiteMode.Strict : SameSiteMode.None,
            Path     = "/api/auth",
            Expires  = DateTimeOffset.UnixEpoch
        });
}
