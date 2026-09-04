using System.Security.Claims;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Services.Interfaces;

public interface ITokenService
{
    /// <summary>Mints a full access token for a user who has completed every step.</summary>
    (string Token, DateTime ExpiresAt) CreateAccessToken(AppUser user, IEnumerable<string> authMethods);

    /// <summary>
    /// Mints the short-lived token issued after the password step. It carries a
    /// purpose claim and no roles, so it opens the 2FA endpoint and nothing else.
    /// </summary>
    (string Token, DateTime ExpiresAt) CreateTwoFactorChallengeToken(AppUser user);

    /// <summary>
    /// Validates a challenge token and returns the user id it names, or null.
    /// Rejects a full access token presented in its place — the purpose claim
    /// must match exactly.
    /// </summary>
    string? ValidateTwoFactorChallengeToken(string token);

    /// <summary>A cryptographically random refresh token and its SHA-256 hash.</summary>
    (string Token, byte[] Hash) CreateRefreshToken();

    /// <summary>Hashes a presented refresh token for lookup. Never stores the token itself.</summary>
    byte[] HashRefreshToken(string token);

    ClaimsPrincipal? ValidateAccessToken(string token);
}
