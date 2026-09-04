using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SaadsShop.Api.Configuration;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Models;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Services.Implementations;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;
    private readonly JwtSecurityTokenHandler _handler = new();

    public TokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        _key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
    }

    public (string Token, DateTime ExpiresAt) CreateAccessToken(AppUser user, IEnumerable<string> authMethods)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(AppClaims.FullName,            user.FullName),
            // A unique id per token, so a specific token can be named in a log
            // or a future deny-list without identifying the user.
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString("N")),
            new(AppClaims.TokenPurpose,        TokenPurposes.Access)
        };

        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        // amr records how this token was actually obtained, so a policy can
        // require that 2FA genuinely happened for THIS session rather than
        // trusting a flag on the user row that may have been set afterwards.
        claims.AddRange(authMethods.Select(m => new Claim(AppClaims.AuthMethod, m)));

        return (Write(claims, expires), expires);
    }

    public (string Token, DateTime ExpiresAt) CreateTwoFactorChallengeToken(AppUser user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.TwoFactorChallengeMinutes);

        // Deliberately no roles. Even if this token were somehow accepted by
        // another endpoint, it would satisfy no authorisation policy.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(AppClaims.TokenPurpose,      TokenPurposes.TwoFactorChallenge)
        };

        return (Write(claims, expires), expires);
    }

    public string? ValidateTwoFactorChallengeToken(string token)
    {
        var principal = Validate(token);
        if (principal is null) return null;

        // The purpose must match exactly. Without this check a full access
        // token would sail through the 2FA endpoint, and the second factor
        // would be bypassable by anyone already holding a session.
        var purpose = principal.FindFirstValue(AppClaims.TokenPurpose);
        if (!string.Equals(purpose, TokenPurposes.TwoFactorChallenge, StringComparison.Ordinal))
            return null;

        return principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        var principal = Validate(token);
        if (principal is null) return null;

        var purpose = principal.FindFirstValue(AppClaims.TokenPurpose);
        return string.Equals(purpose, TokenPurposes.Access, StringComparison.Ordinal) ? principal : null;
    }

    public (string Token, byte[] Hash) CreateRefreshToken()
    {
        // 32 bytes from the OS CSPRNG. Base64url so it survives a cookie
        // without escaping.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncoder.Encode(bytes);

        return (token, HashRefreshToken(token));
    }

    /// <summary>
    /// Plain SHA-256, deliberately — not a password hash.
    /// </summary>
    /// <remarks>
    /// A refresh token is 256 bits of uniform randomness, so there is no
    /// dictionary to attack and nothing for a slow KDF to defend against. What
    /// matters is that the stored form is useless if the table leaks, and that
    /// lookup is fast enough to sit on the hot path of every refresh. Argon2
    /// here would cost real latency and buy nothing.
    /// </remarks>
    public byte[] HashRefreshToken(string token)
        => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private string Write(IEnumerable<Claim> claims, DateTime expires)
    {
        var descriptor = new JwtSecurityToken(
            issuer:             _options.Issuer,
            audience:           _options.Audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            expires,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return _handler.WriteToken(descriptor);
    }

    private ClaimsPrincipal? Validate(string token)
    {
        try
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidIssuer              = _options.Issuer,
                ValidateAudience         = true,
                ValidAudience            = _options.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = _key,
                ValidateLifetime         = true,

                // The framework default is five minutes, which would make a
                // "15-minute" token live for twenty. Thirty seconds covers
                // ordinary clock drift and nothing more.
                ClockSkew                = TimeSpan.FromSeconds(30),

                // Pin the algorithm. Without this an attacker could present a
                // token signed with something weaker and have it accepted.
                ValidAlgorithms          = [SecurityAlgorithms.HmacSha256]
            };

            return _handler.ValidateToken(token, parameters, out _);
        }
        catch (SecurityTokenException)
        {
            // Expired, tampered, wrong issuer — all the same answer to the
            // caller. The specific reason goes nowhere near the response.
            return null;
        }
        catch (ArgumentException)
        {
            // Malformed input that is not even shaped like a JWT.
            return null;
        }
    }
}
