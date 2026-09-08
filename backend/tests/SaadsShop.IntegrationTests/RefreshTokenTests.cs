using System.Security.Cryptography;
using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Repositories.Implementations.Commands;

namespace SaadsShop.IntegrationTests;

/// <summary>
/// Rotation and reuse detection, against the procedure that implements them.
/// </summary>
/// <remarks>
/// The other half of what <c>database/README.md</c> describes as run by hand.
/// The rule is that a refresh token is good exactly once: presenting a spent
/// one is evidence that somebody copied it, so the whole family dies rather
/// than only the token — the thief and the customer are both signed out, and
/// the customer signs back in.
/// </remarks>
[Collection(ShopDatabaseCollection.Name)]
public class RefreshTokenTests(ShopDatabase db)
{
    private IdentityCommandRepository Identity => new(db.Connections);

    private static byte[] AHash() => RandomNumberGenerator.GetBytes(32);
    private static DateTime InTwoWeeks => DateTime.UtcNow.AddDays(14);

    /// <summary>A user with one live refresh token, as a fresh sign-in leaves them.</summary>
    private async Task<(string UserId, Guid Family, byte[] Hash)> ASignedInUserAsync()
    {
        var userId = await db.AUserAsync();
        var family = Guid.NewGuid();
        var hash   = AHash();

        var created = await Identity.CreateRefreshTokenAsync(userId, hash, family, InTwoWeeks, "127.0.0.1");
        Assert.True(created.IsSuccess);

        return (userId, family, hash);
    }

    /// <summary>
    /// Tokens in the family that could still be redeemed.
    /// </summary>
    /// <remarks>
    /// Both columns matter. Rotation marks the old token <c>UsedAt</c> and
    /// leaves <c>RevokedAt</c> alone — the row stays as evidence, and it is
    /// what a later replay is detected against — so counting only unrevoked
    /// rows counts spent tokens as live.
    /// </remarks>
    private async Task<int> UsableTokenCountAsync(Guid family)
    {
        await using var connection = await db.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.RefreshTokens
            WHERE  FamilyId = @family AND RevokedAt IS NULL AND UsedAt IS NULL
            """,
            new { family });
    }

    [Fact]
    public async Task A_fresh_token_can_be_redeemed_once()
    {
        var (userId, _, hash) = await ASignedInUserAsync();

        var redeemed = await Identity.RedeemRefreshTokenAsync(hash, AHash(), InTwoWeeks, "127.0.0.1");

        Assert.True(redeemed.IsSuccess);
        Assert.Equal(userId, redeemed.Data!.UserId);
        Assert.False(redeemed.Data.ReuseDetected);
    }

    [Fact]
    public async Task Redeeming_hands_back_who_it_was_and_what_they_may_do()
    {
        // The API builds the access token from this, so a missing role here is
        // a shopkeeper locked out of their own panel.
        var (_, _, hash) = await ASignedInUserAsync();

        var redeemed = await Identity.RedeemRefreshTokenAsync(hash, AHash(), InTwoWeeks, null);

        Assert.NotNull(redeemed.Data!.Email);
        Assert.Contains("Staff", redeemed.Data.Roles);
    }

    [Fact]
    public async Task The_rotated_token_is_the_one_that_works_next()
    {
        var (_, _, hash) = await ASignedInUserAsync();
        var second = AHash();

        await Identity.RedeemRefreshTokenAsync(hash, second, InTwoWeeks, null);
        var again = await Identity.RedeemRefreshTokenAsync(second, AHash(), InTwoWeeks, null);

        Assert.True(again.IsSuccess);
    }

    [Fact]
    public async Task Presenting_a_spent_token_fails()
    {
        var (_, _, hash) = await ASignedInUserAsync();

        await Identity.RedeemRefreshTokenAsync(hash, AHash(), InTwoWeeks, null);
        var replay = await Identity.RedeemRefreshTokenAsync(hash, AHash(), InTwoWeeks, null);

        Assert.False(replay.IsSuccess);
        Assert.Equal(ResponseCodes.Unauthorised, replay.ResponseCode);
    }

    [Fact]
    public async Task Presenting_a_spent_token_says_so_rather_than_just_failing()
    {
        // The API logs a security event on this flag; an ordinary expiry is not
        // the same thing as a stolen token being used.
        var (_, _, hash) = await ASignedInUserAsync();

        await Identity.RedeemRefreshTokenAsync(hash, AHash(), InTwoWeeks, null);
        var replay = await Identity.RedeemRefreshTokenAsync(hash, AHash(), InTwoWeeks, null);

        Assert.True(replay.Data!.ReuseDetected);
    }

    [Fact]
    public async Task A_replay_kills_the_whole_family_not_just_the_token()
    {
        var (_, family, first) = await ASignedInUserAsync();

        var second = AHash();
        await Identity.RedeemRefreshTokenAsync(first, second, InTwoWeeks, null);
        var third = AHash();
        await Identity.RedeemRefreshTokenAsync(second, third, InTwoWeeks, null);

        Assert.Equal(1, await UsableTokenCountAsync(family));

        // Somebody replays the token from two rotations ago.
        await Identity.RedeemRefreshTokenAsync(first, AHash(), InTwoWeeks, null);

        Assert.Equal(0, await UsableTokenCountAsync(family));
    }

    [Fact]
    public async Task After_a_replay_even_the_newest_token_is_dead()
    {
        // This is the point of the family revoke: the thief rotated, so the
        // customer's own next refresh must fail too and send them to sign-in.
        var (_, _, first) = await ASignedInUserAsync();

        var second = AHash();
        await Identity.RedeemRefreshTokenAsync(first, second, InTwoWeeks, null);

        await Identity.RedeemRefreshTokenAsync(first, AHash(), InTwoWeeks, null);
        var honest = await Identity.RedeemRefreshTokenAsync(second, AHash(), InTwoWeeks, null);

        Assert.False(honest.IsSuccess);
    }

    [Fact]
    public async Task One_family_dying_leaves_the_other_devices_alone()
    {
        // The shopkeeper's phone must not be signed out because the counter
        // laptop was compromised... it is a different family.
        var userId  = await db.AUserAsync();
        var phone   = Guid.NewGuid();
        var laptop  = Guid.NewGuid();
        var onPhone  = AHash();
        var onLaptop = AHash();

        await Identity.CreateRefreshTokenAsync(userId, onPhone,  phone,  InTwoWeeks, null);
        await Identity.CreateRefreshTokenAsync(userId, onLaptop, laptop, InTwoWeeks, null);

        await Identity.RedeemRefreshTokenAsync(onLaptop, AHash(), InTwoWeeks, null);
        await Identity.RedeemRefreshTokenAsync(onLaptop, AHash(), InTwoWeeks, null);   // the replay

        Assert.Equal(0, await UsableTokenCountAsync(laptop));
        Assert.Equal(1, await UsableTokenCountAsync(phone));
    }

    [Fact]
    public async Task An_unknown_token_is_refused_without_saying_why()
    {
        var redeemed = await Identity.RedeemRefreshTokenAsync(AHash(), AHash(), InTwoWeeks, null);

        Assert.Equal(ResponseCodes.Unauthorised, redeemed.ResponseCode);
        // Nothing was stolen — there is no family to revoke and nothing to log.
        Assert.False(redeemed.Data?.ReuseDetected ?? false);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var userId = await db.AUserAsync();
        var hash   = AHash();

        await Identity.CreateRefreshTokenAsync(
            userId, hash, Guid.NewGuid(), DateTime.UtcNow.AddDays(-1), null);

        var redeemed = await Identity.RedeemRefreshTokenAsync(hash, AHash(), InTwoWeeks, null);

        Assert.False(redeemed.IsSuccess);
    }

    [Fact]
    public async Task A_deactivated_account_cannot_refresh_its_way_back_in()
    {
        var (userId, _, hash) = await ASignedInUserAsync();

        await using (var connection = await db.OpenAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.Users SET IsActive = 0 WHERE Id = @userId", new { userId });
        }

        var redeemed = await Identity.RedeemRefreshTokenAsync(hash, AHash(), InTwoWeeks, null);

        Assert.False(redeemed.IsSuccess);
    }

    [Fact]
    public async Task Signing_out_revokes_the_token_that_was_presented()
    {
        var (_, family, hash) = await ASignedInUserAsync();

        await Identity.RevokeRefreshTokensAsync(hash, null, null, "Signed out");

        Assert.Equal(0, await UsableTokenCountAsync(family));
    }

    [Fact]
    public async Task Signing_out_everywhere_revokes_every_device()
    {
        var userId = await db.AUserAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await Identity.CreateRefreshTokenAsync(userId, AHash(), a, InTwoWeeks, null);
        await Identity.CreateRefreshTokenAsync(userId, AHash(), b, InTwoWeeks, null);

        await Identity.RevokeRefreshTokensAsync(null, userId, null, "Password changed");

        Assert.Equal(0, await UsableTokenCountAsync(a));
        Assert.Equal(0, await UsableTokenCountAsync(b));
    }

    [Fact]
    public async Task Ten_tabs_refreshing_at_once_never_yields_two_live_tokens()
    {
        //  The race the shop actually hit: several tabs refresh together, one
        //  wins the rotation and the rest present a token that is now spent.
        //  Whatever the outcome, the family must not end up with two live
        //  tokens — that would mean the same session rotating in two
        //  directions.
        var (_, family, hash) = await ASignedInUserAsync();

        await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Identity.RedeemRefreshTokenAsync(hash, AHash(), InTwoWeeks, null)));

        Assert.InRange(await UsableTokenCountAsync(family), 0, 1);
    }
}
