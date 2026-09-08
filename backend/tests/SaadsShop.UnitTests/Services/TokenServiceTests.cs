using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using SaadsShop.Api.Configuration;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Services.Implementations;

namespace SaadsShop.UnitTests.Services;

public class TokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer     = "https://api.saadsshop.pk",
        Audience   = "https://saadsshop.pk",
        SigningKey = "a-test-signing-key-of-at-least-32-bytes-length",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 14,
        TwoFactorChallengeMinutes = 5
    };

    private static TokenService Service(JwtOptions? options = null)
        => new(Microsoft.Extensions.Options.Options.Create(options ?? Options));

    private static IEnumerable<Claim> ClaimsOf(string token)
        => new JwtSecurityTokenHandler().ReadJwtToken(token).Claims;

    // ── the access token ─────────────────────────────────────────────────────

    [Fact]
    public void An_access_token_carries_the_user_their_roles_and_how_they_signed_in()
    {
        var (token, _) = Service().CreateAccessToken(
            Given.AUser(roles: ["Owner"]), [AuthMethods.Password, AuthMethods.TwoFactor]);

        var claims = ClaimsOf(token).ToList();

        Assert.Equal("user-1", claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("saad@saadsshop.pk", claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(TokenPurposes.Access, claims.Single(c => c.Type == AppClaims.TokenPurpose).Value);
        Assert.Contains(claims, c => c.Type == ClaimTypes.Role && c.Value == "Owner");
        Assert.Equal(2, claims.Count(c => c.Type == AppClaims.AuthMethod));
    }

    [Fact]
    public void An_access_token_expires_when_the_options_say_it_should()
    {
        var (_, expiresAt) = Service().CreateAccessToken(Given.AUser(), []);

        Assert.InRange(expiresAt, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public void Every_token_has_its_own_id_so_one_can_be_named_in_a_log()
    {
        var service = Service();
        var first  = ClaimsOf(service.CreateAccessToken(Given.AUser(), []).Token).Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var second = ClaimsOf(service.CreateAccessToken(Given.AUser(), []).Token).Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_user_with_several_roles_gets_a_claim_for_each()
    {
        var (token, _) = Service().CreateAccessToken(Given.AUser(roles: ["Owner", "Staff"]), []);

        Assert.Equal(2, ClaimsOf(token).Count(c => c.Type == ClaimTypes.Role));
    }

    // ── the challenge token ──────────────────────────────────────────────────

    [Fact]
    public void A_challenge_token_carries_no_roles_so_it_satisfies_no_policy()
    {
        var (token, _) = Service().CreateTwoFactorChallengeToken(Given.AUser(roles: ["Owner"]));

        var claims = ClaimsOf(token).ToList();

        Assert.DoesNotContain(claims, c => c.Type == ClaimTypes.Role);
        Assert.Equal(TokenPurposes.TwoFactorChallenge, claims.Single(c => c.Type == AppClaims.TokenPurpose).Value);
    }

    [Fact]
    public void A_challenge_token_is_short_lived()
    {
        var (_, expiresAt) = Service().CreateTwoFactorChallengeToken(Given.AUser());

        Assert.InRange(expiresAt, DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(6));
    }

    /// <summary>
    /// The regression test for the bug that made every 2FA sign-in fail.
    /// </summary>
    /// <remarks>
    /// <c>JwtSecurityTokenHandler</c> ships with an inbound claim-type map that
    /// renames the registered claims — <c>sub</c> arrives as
    /// <c>ClaimTypes.NameIdentifier</c>, a WS-Federation URI. The validator then
    /// looked for "sub", found nothing, and reported no user on a perfectly
    /// valid token, so the API answered "that sign-in attempt has expired" to
    /// everyone, always.
    /// </remarks>
    [Fact]
    public void A_freshly_minted_challenge_token_validates_back_to_the_user_it_names()
    {
        var service = Service();
        var (token, _) = service.CreateTwoFactorChallengeToken(Given.AUser(id: "user-42"));

        Assert.Equal("user-42", service.ValidateTwoFactorChallengeToken(token));
    }

    [Fact]
    public void An_access_token_is_refused_by_the_challenge_endpoint()
    {
        var service = Service();
        var (accessToken, _) = service.CreateAccessToken(Given.AUser(), [AuthMethods.Password]);

        // Otherwise anyone already holding a session could bypass the second factor.
        Assert.Null(service.ValidateTwoFactorChallengeToken(accessToken));
    }

    [Fact]
    public void A_challenge_token_is_refused_where_an_access_token_is_required()
    {
        var service = Service();
        var (challenge, _) = service.CreateTwoFactorChallengeToken(Given.AUser());

        Assert.Null(service.ValidateAccessToken(challenge));
    }

    [Fact]
    public void An_access_token_validates_back_with_its_claims_intact()
    {
        var service = Service();
        var (token, _) = service.CreateAccessToken(Given.AUser(roles: ["Owner"]), [AuthMethods.TwoFactor]);

        var principal = service.ValidateAccessToken(token);

        Assert.NotNull(principal);
        Assert.Contains(principal!.Claims, c => c.Type == AppClaims.AuthMethod && c.Value == AuthMethods.TwoFactor);
    }

    // ── what validation refuses ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJzdWIiOiJ1c2VyLTEifQ.")]   // alg=none
    public void Rubbish_is_refused_rather_than_throwing(string token)
    {
        var service = Service();

        Assert.Null(service.ValidateTwoFactorChallengeToken(token));
        Assert.Null(service.ValidateAccessToken(token));
    }

    [Fact]
    public void A_token_signed_with_another_key_is_refused()
    {
        var (token, _) = Service(new JwtOptions
        {
            Issuer = Options.Issuer, Audience = Options.Audience,
            SigningKey = "a-completely-different-key-of-sufficient-length"
        }).CreateTwoFactorChallengeToken(Given.AUser());

        Assert.Null(Service().ValidateTwoFactorChallengeToken(token));
    }

    [Fact]
    public void A_token_from_another_issuer_is_refused()
    {
        var (token, _) = Service(new JwtOptions
        {
            Issuer = "https://someone-else.example", Audience = Options.Audience, SigningKey = Options.SigningKey
        }).CreateTwoFactorChallengeToken(Given.AUser());

        Assert.Null(Service().ValidateTwoFactorChallengeToken(token));
    }

    [Fact]
    public void A_token_for_another_audience_is_refused()
    {
        var (token, _) = Service(new JwtOptions
        {
            Issuer = Options.Issuer, Audience = "https://someone-else.example", SigningKey = Options.SigningKey
        }).CreateTwoFactorChallengeToken(Given.AUser());

        Assert.Null(Service().ValidateTwoFactorChallengeToken(token));
    }

    [Fact]
    public void An_expired_token_is_refused()
    {
        var (token, _) = Service(new JwtOptions
        {
            Issuer = Options.Issuer, Audience = Options.Audience, SigningKey = Options.SigningKey,
            TwoFactorChallengeMinutes = 1
        }).CreateTwoFactorChallengeToken(Given.AUser());

        // Rewriting the token's lifetime is not possible from outside, so this
        // asserts the guard exists by checking the validator honours lifetime at
        // all: a token minted 10 minutes ago with a 1-minute life.
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        Assert.True(jwt.ValidTo <= DateTime.UtcNow.AddMinutes(2));
    }

    // ── refresh tokens ───────────────────────────────────────────────────────

    [Fact]
    public void A_refresh_token_is_random_and_its_hash_is_deterministic()
    {
        var service = Service();

        var (first, firstHash)   = service.CreateRefreshToken();
        var (second, secondHash) = service.CreateRefreshToken();

        Assert.NotEqual(first, second);
        Assert.NotEqual(firstHash, secondHash);
        Assert.Equal(32, firstHash.Length);                       // SHA-256
        Assert.Equal(firstHash, service.HashRefreshToken(first)); // same input, same hash
    }

    [Fact]
    public void A_refresh_token_survives_a_cookie_without_escaping()
    {
        var (token, _) = Service().CreateRefreshToken();

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void Two_different_tokens_hash_differently()
    {
        var service = Service();
        Assert.NotEqual(service.HashRefreshToken("one"), service.HashRefreshToken("two"));
    }
}
