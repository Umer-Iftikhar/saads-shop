using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NSubstitute;
using SaadsShop.Api.Configuration;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Repositories.Interfaces.Commands;
using SaadsShop.Api.Repositories.Interfaces.Queries;
using SaadsShop.Api.Services.Implementations;
using SaadsShop.Api.Services.Implementations.Commands;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.UnitTests.Services;

public class AuthCommandServiceTests
{
    private readonly IIdentityQueryRepository   _reader = Substitute.For<IIdentityQueryRepository>();
    private readonly IIdentityCommandRepository _writer = Substitute.For<IIdentityCommandRepository>();
    private readonly ITokenService     _tokens    = Substitute.For<ITokenService>();
    private readonly ITwoFactorService _twoFactor = Substitute.For<ITwoFactorService>();
    private readonly IPasswordHasher<AppUser> _hasher = new PasswordHasher<AppUser>();

    //  One provider shared with the service under test. Two ephemeral providers
    //  hold different keys, so ciphertext from one is unreadable by the other —
    //  which looks exactly like "no authenticator enrolled" and would make these
    //  tests assert the wrong branch.
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

    private string ProtectedSecret(string secret = "SECRET")
        => _protection.CreateProtector("SaadsShop.Totp.v1").Protect(secret);

    private static readonly JwtOptions Jwt = new()
    {
        Issuer = "https://api.saadsshop.pk", Audience = "https://saadsshop.pk",
        SigningKey = "a-test-signing-key-of-at-least-32-bytes-length",
        RefreshTokenDays = 14, RefreshRotationGraceSeconds = 20
    };

    private static readonly AuthOptions Auth = new()
    {
        MaxFailedAccessAttempts = 3, LockoutMinutes = 15
    };

    private AuthCommandService Service(JwtOptions? jwt = null) => new(
        _reader, _writer, _tokens, _twoFactor, _hasher,
        _protection,
        Options.Create(jwt ?? Jwt), Options.Create(Auth),
        Given.Log<AuthCommandService>());

    /// <summary>The static grace-window table is process-wide; each test starts from empty.</summary>
    private static void ClearRotationWindow()
    {
        var field = typeof(AuthCommandService)
            .GetField("RecentRotations", BindingFlags.NonPublic | BindingFlags.Static)!;

        var dictionary = field.GetValue(null)!;
        dictionary.GetType().GetMethod("Clear")!.Invoke(dictionary, null);
    }

    private AppUser AUserWithPassword(string password = "ShopOwner!2026pk", bool isActive = true,
                                      bool twoFactorEnabled = true, int failedCount = 0,
                                      DateTimeOffset? lockoutEnd = null)
    {
        var user = Given.AUser(isActive: isActive, twoFactorEnabled: twoFactorEnabled);
        user.PasswordHash      = _hasher.HashPassword(user, password);
        user.AccessFailedCount = failedCount;
        user.LockoutEnd        = lockoutEnd;
        return user;
    }

    private void GivenUser(AppUser? user)
        => _reader.FindUserAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                  .Returns(Given.Ok(user));

    private void GivenChallengeTokenIssued()
        => _tokens.CreateTwoFactorChallengeToken(Arg.Any<AppUser>())
                  .Returns(("challenge-token", DateTime.UtcNow.AddMinutes(5)));

    // ── sign-in: the password step ───────────────────────────────────────────

    [Fact]
    public async Task A_correct_password_yields_a_challenge_and_never_an_access_token()
    {
        GivenUser(AUserWithPassword());
        GivenChallengeTokenIssued();

        var result = await Service().LoginAsync(
            new LoginRequest { Email = "saad@saadsshop.pk", Password = "ShopOwner!2026pk" }, "1.2.3.4");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.RequiresTwoFactor);
        Assert.Equal("challenge-token", result.Value.MfaToken);

        _tokens.DidNotReceiveWithAnyArgs().CreateAccessToken(default!, default!);
    }

    [Fact]
    public async Task The_challenge_says_whether_an_authenticator_is_enrolled()
    {
        GivenUser(AUserWithPassword(twoFactorEnabled: false));
        GivenChallengeTokenIssued();

        var result = await Service().LoginAsync(
            new LoginRequest { Email = "saad@saadsshop.pk", Password = "ShopOwner!2026pk" }, null);

        // Otherwise a new account is sent to a code prompt it cannot answer.
        Assert.False(result.Value!.IsTwoFactorEnrolled);
    }

    [Fact]
    public async Task An_unknown_account_and_a_wrong_password_answer_identically()
    {
        GivenUser(null);
        var unknown = await Service().LoginAsync(
            new LoginRequest { Email = "nobody@saadsshop.pk", Password = "whatever" }, null);

        GivenUser(AUserWithPassword());
        var wrongPassword = await Service().LoginAsync(
            new LoginRequest { Email = "saad@saadsshop.pk", Password = "not-the-password" }, null);

        Assert.Equal(unknown.ResponseCode, wrongPassword.ResponseCode);
        Assert.Equal(unknown.Message, wrongPassword.Message);
        Assert.Equal("That email and password do not match.", unknown.Message);
    }

    [Fact]
    public async Task A_disabled_account_answers_the_same_way_again()
    {
        GivenUser(AUserWithPassword(isActive: false));

        var result = await Service().LoginAsync(
            new LoginRequest { Email = "saad@saadsshop.pk", Password = "ShopOwner!2026pk" }, null);

        Assert.Equal(401, result.ResponseCode);
        Assert.Equal("That email and password do not match.", result.Message);
    }

    [Fact]
    public async Task An_account_with_no_password_cannot_be_signed_into()
    {
        var user = Given.AUser();
        user.PasswordHash = null;
        GivenUser(user);

        Assert.Equal(401, (await Service().LoginAsync(
            new LoginRequest { Email = "saad@saadsshop.pk", Password = "x" }, null)).ResponseCode);
    }

    [Fact]
    public async Task The_email_is_matched_case_insensitively()
    {
        GivenUser(AUserWithPassword());
        GivenChallengeTokenIssued();

        await Service().LoginAsync(
            new LoginRequest { Email = "  SAAD@SaadsShop.PK  ", Password = "ShopOwner!2026pk" }, null);

        await _reader.Received(1).FindUserAsync(
            null, "SAAD@SAADSSHOP.PK", null, Arg.Any<CancellationToken>());
    }

    // ── lockout ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_wrong_password_records_the_attempt()
    {
        GivenUser(AUserWithPassword(failedCount: 0));

        await Service().LoginAsync(new LoginRequest { Email = "a@b.pk", Password = "wrong" }, "1.2.3.4");

        await _writer.Received(1).UpdateUserAsync(
            Arg.Is<UserUpdate>(u => u.AccessFailedCount == 1 && u.LockoutEnd == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_attempt_that_reaches_the_limit_locks_the_account()
    {
        GivenUser(AUserWithPassword(failedCount: 2));   // limit is 3

        await Service().LoginAsync(new LoginRequest { Email = "a@b.pk", Password = "wrong" }, null);

        await _writer.Received(1).UpdateUserAsync(
            Arg.Is<UserUpdate>(u => u.AccessFailedCount == 3 && u.LockoutEnd != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_locked_account_is_told_to_wait_rather_than_that_the_password_was_wrong()
    {
        GivenUser(AUserWithPassword(lockoutEnd: DateTimeOffset.UtcNow.AddMinutes(10)));

        var result = await Service().LoginAsync(
            new LoginRequest { Email = "a@b.pk", Password = "ShopOwner!2026pk" }, null);

        Assert.Equal(401, result.ResponseCode);
        Assert.Contains("Too many attempts", result.Message);
    }

    [Fact]
    public async Task An_expired_lockout_no_longer_blocks_the_account()
    {
        GivenUser(AUserWithPassword(lockoutEnd: DateTimeOffset.UtcNow.AddMinutes(-1)));
        GivenChallengeTokenIssued();

        Assert.True((await Service().LoginAsync(
            new LoginRequest { Email = "a@b.pk", Password = "ShopOwner!2026pk" }, null)).IsSuccess);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_a_partial_failure_count()
    {
        GivenUser(AUserWithPassword(failedCount: 2));
        GivenChallengeTokenIssued();

        await Service().LoginAsync(new LoginRequest { Email = "a@b.pk", Password = "ShopOwner!2026pk" }, null);

        await _writer.Received(1).UpdateUserAsync(
            Arg.Is<UserUpdate>(u => u.AccessFailedCount == 0 && u.ClearLockout),
            Arg.Any<CancellationToken>());
    }

    // ── the second factor ────────────────────────────────────────────────────

    [Fact]
    public async Task A_challenge_token_that_names_nobody_is_refused()
    {
        _tokens.ValidateTwoFactorChallengeToken(Arg.Any<string>()).Returns((string?)null);

        var result = await Service().VerifyTwoFactorAsync(
            new TwoFactorRequest { MfaToken = "stale", Code = "123456" }, null);

        Assert.Equal(401, result.ResponseCode);
        Assert.Contains("expired", result.Message);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_counts_against_the_lockout()
    {
        _tokens.ValidateTwoFactorChallengeToken(Arg.Any<string>()).Returns("user-1");
        GivenUser(AUserWithPassword());
        _reader.GetTokenAsync("user-1", TokenStore.Provider, TokenStore.AuthenticatorKey, Arg.Any<CancellationToken>())
               .Returns(Given.Ok<string?>(ProtectedSecret()));
        _twoFactor.VerifyCode(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var result = await Service().VerifyTwoFactorAsync(
            new TwoFactorRequest { MfaToken = "t", Code = "000000" }, null);

        Assert.Equal(401, result.ResponseCode);
        Assert.Equal("That code is not valid.", result.Message);

        await _writer.Received().UpdateUserAsync(
            Arg.Is<UserUpdate>(u => u.AccessFailedCount == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_account_with_no_authenticator_key_cannot_verify_a_code()
    {
        _tokens.ValidateTwoFactorChallengeToken(Arg.Any<string>()).Returns("user-1");
        GivenUser(AUserWithPassword());
        _reader.GetTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok<string?>(null));

        var result = await Service().VerifyTwoFactorAsync(
            new TwoFactorRequest { MfaToken = "t", Code = "123456" }, null);

        Assert.Equal(401, result.ResponseCode);
        Assert.Contains("not set up", result.Message);
    }

    [Fact]
    public async Task A_spent_recovery_code_is_refused()
    {
        _tokens.ValidateTwoFactorChallengeToken(Arg.Any<string>()).Returns("user-1");
        GivenUser(AUserWithPassword());
        _twoFactor.HashRecoveryCode(Arg.Any<string>()).Returns([1, 2, 3]);
        _writer.RedeemRecoveryCodeAsync("user-1", Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
               .Returns(Given.Failed<int>(401, "That code is not valid."));

        var result = await Service().VerifyTwoFactorAsync(
            new TwoFactorRequest { MfaToken = "t", Code = "abcd-efgh", IsRecoveryCode = true }, null);

        Assert.Equal(401, result.ResponseCode);
    }

    [Fact]
    public async Task A_good_recovery_code_issues_a_session_marked_as_two_factor()
    {
        _tokens.ValidateTwoFactorChallengeToken(Arg.Any<string>()).Returns("user-1");
        GivenUser(AUserWithPassword());
        _twoFactor.HashRecoveryCode(Arg.Any<string>()).Returns([1, 2, 3]);
        _writer.RedeemRecoveryCodeAsync("user-1", Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok(9));
        _tokens.CreateAccessToken(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>())
               .Returns(("access", DateTime.UtcNow.AddMinutes(15)));
        _tokens.CreateRefreshToken().Returns(("refresh", new byte[32]));
        _writer.CreateRefreshTokenAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<Guid>(),
                                       Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok<long?>(1));

        var result = await Service().VerifyTwoFactorAsync(
            new TwoFactorRequest { MfaToken = "t", Code = "abcd-efgh", IsRecoveryCode = true }, null);

        Assert.True(result.IsSuccess);
        _tokens.Received(1).CreateAccessToken(
            Arg.Any<AppUser>(),
            Arg.Is<IEnumerable<string>>(m => m.Contains(AuthMethods.TwoFactor) && m.Contains(AuthMethods.RecoveryCode)));
    }

    // ── Google ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unverified_google_email_is_refused()
    {
        var result = await Service().ExternalLoginAsync(
            "Google", "key", "someone@gmail.com", emailVerified: false, "Someone", null);

        Assert.Equal(401, result.ResponseCode);
        await _reader.DidNotReceiveWithAnyArgs().FindUserAsync(default, default, default, default);
    }

    [Fact]
    public async Task Google_never_provisions_a_new_staff_account()
    {
        GivenUser(null);

        var result = await Service().ExternalLoginAsync(
            "Google", "key", "stranger@gmail.com", emailVerified: true, "Stranger", null);

        Assert.Equal(403, result.ResponseCode);
        Assert.Contains("Ask the owner", result.Message);
        await _writer.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default);
    }

    [Fact]
    public async Task Google_signs_in_a_known_account_but_still_demands_the_second_factor()
    {
        GivenUser(AUserWithPassword());
        GivenChallengeTokenIssued();

        var result = await Service().ExternalLoginAsync(
            "Google", "key-1", "saad@saadsshop.pk", emailVerified: true, "Saad", null);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.RequiresTwoFactor);
        await _writer.Received(1).AddExternalLoginAsync("user-1", "Google", "key-1", "Saad", Arg.Any<CancellationToken>());
    }

    // ── staff accounts ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_staff_account_stores_a_hash_and_never_the_password()
    {
        _writer.CreateUserAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok(true));

        await Service().CreateStaffAsync(new CreateStaffRequest
        {
            FullName = "Nasir", Email = "nasir@saadsshop.pk", Password = "AGoodPassword!1", Role = "Staff"
        });

        await _writer.Received(1).CreateUserAsync(
            Arg.Is<AppUser>(u => u.PasswordHash != null && !u.PasswordHash.Contains("AGoodPassword")),
            "Staff", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_new_staff_account_normalises_its_email_and_phone()
    {
        _writer.CreateUserAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok(true));

        await Service().CreateStaffAsync(new CreateStaffRequest
        {
            FullName = "Nasir", Email = " Nasir@SaadsShop.pk ", Password = "AGoodPassword!1",
            PhoneNumber = "+92 301 234 5678", Role = "Staff"
        });

        await _writer.Received(1).CreateUserAsync(
            Arg.Is<AppUser>(u => u.Email == "Nasir@SaadsShop.pk"
                              && u.NormalizedEmail == "NASIR@SAADSSHOP.PK"
                              && u.PhoneNumber == "03012345678"),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_bad_phone_number_stops_the_account_being_created()
    {
        var result = await Service().CreateStaffAsync(new CreateStaffRequest
        {
            FullName = "Nasir", Email = "nasir@saadsshop.pk", Password = "AGoodPassword!1",
            PhoneNumber = "12345", Role = "Staff"
        });

        Assert.Equal(400, result.ResponseCode);
        await _writer.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default);
    }

    [Fact]
    public async Task The_last_owner_cannot_be_demoted_and_the_procedures_reason_is_passed_on()
    {
        _writer.SetRoleAsync("user-1", "Owner", false, Arg.Any<CancellationToken>())
               .Returns(Given.Failed<bool>(409, "The shop must keep at least one Owner."));

        var result = await Service().SetRoleAsync(new SetRoleRequest
        {
            UserId = "user-1", Role = "Owner", Attach = false
        });

        Assert.Equal(409, result.ResponseCode);
        Assert.Equal("The shop must keep at least one Owner.", result.Message);
    }

    // ── signing out ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Signing_out_with_a_cookie_revokes_by_token_so_the_family_goes_with_it()
    {
        _tokens.HashRefreshToken("the-cookie").Returns([9, 9, 9]);
        _writer.RevokeRefreshTokensAsync(Arg.Any<byte[]?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok(1));

        await Service().LogoutAsync("the-cookie", "user-1");

        await _writer.Received(1).RevokeRefreshTokensAsync(
            Arg.Is<byte[]?>(h => h != null), null, null, "Signed out", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Signing_out_without_a_cookie_revokes_everything_the_user_holds()
    {
        _writer.RevokeRefreshTokensAsync(Arg.Any<byte[]?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok(2));

        await Service().LogoutAsync(null, "user-1");

        await _writer.Received(1).RevokeRefreshTokensAsync(
            null, "user-1", null, "Signed out", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Signing_out_reports_success_even_when_there_was_nothing_to_revoke()
        => Assert.True((await Service().LogoutAsync(null, "user-1")).IsSuccess);

    // ── rotation, and the 20-second window ───────────────────────────────────

    private void GivenRotationSucceeds(string issuedToken = "new-refresh")
    {
        _tokens.HashRefreshToken(Arg.Any<string>()).Returns(c => System.Text.Encoding.UTF8.GetBytes(
            ((string)c[0]).PadRight(32, '.')[..32]));
        _tokens.CreateRefreshToken().Returns((issuedToken, new byte[32]));
        _tokens.CreateAccessToken(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>())
               .Returns(("access-token", DateTime.UtcNow.AddMinutes(15)));

        _writer.RedeemRefreshTokenAsync(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok(new RefreshRedemption
               {
                   UserId = "user-1", Email = "saad@saadsshop.pk", FullName = "Saad", Roles = ["Owner"]
               }));
    }

    [Fact]
    public async Task A_rotation_returns_a_new_access_and_refresh_token()
    {
        ClearRotationWindow();
        GivenRotationSucceeds();

        var result = await Service().RefreshAsync("the-old-token", null);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-refresh", result.Value!.RefreshToken);
        Assert.Equal("access-token", result.Value.Auth.AccessToken);
    }

    [Fact]
    public async Task A_refreshed_session_inherits_password_and_two_factor()
    {
        ClearRotationWindow();
        GivenRotationSucceeds();

        await Service().RefreshAsync("the-old-token", null);

        // Forcing 2FA again every fifteen minutes would make the shop unusable.
        _tokens.Received(1).CreateAccessToken(
            Arg.Any<AppUser>(),
            Arg.Is<IEnumerable<string>>(m => m.Contains(AuthMethods.Password) && m.Contains(AuthMethods.TwoFactor)));
    }

    /// <summary>
    /// The regression test for the race that signed the shop out.
    /// </summary>
    [Fact]
    public async Task Eight_refreshes_at_once_on_one_token_produce_a_single_rotation()
    {
        ClearRotationWindow();
        GivenRotationSucceeds();

        var service = Service();
        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => service.RefreshAsync("the-same-token", null)));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Single(results.Select(r => r.Value!.RefreshToken).Distinct());

        // One rotation reached the database; the other seven shared its answer.
        await _writer.Received(1).RedeemRefreshTokenAsync(
            Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_retry_inside_the_window_is_answered_with_the_same_tokens()
    {
        ClearRotationWindow();
        GivenRotationSucceeds();

        var service = Service();
        var first  = await service.RefreshAsync("the-same-token", null);
        var second = await service.RefreshAsync("the-same-token", null);

        Assert.Equal(first.Value!.RefreshToken, second.Value!.RefreshToken);
        await _writer.Received(1).RedeemRefreshTokenAsync(
            Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Two_different_tokens_each_get_their_own_rotation()
    {
        ClearRotationWindow();
        GivenRotationSucceeds();

        var service = Service();
        await service.RefreshAsync("token-one", null);
        await service.RefreshAsync("token-two", null);

        await _writer.Received(2).RedeemRefreshTokenAsync(
            Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_the_window_switched_off_every_call_reaches_the_database()
    {
        ClearRotationWindow();
        GivenRotationSucceeds();

        var service = Service(new JwtOptions
        {
            Issuer = Jwt.Issuer, Audience = Jwt.Audience, SigningKey = Jwt.SigningKey,
            RefreshRotationGraceSeconds = 0
        });

        await service.RefreshAsync("the-same-token", null);
        await service.RefreshAsync("the-same-token", null);

        await _writer.Received(2).RedeemRefreshTokenAsync(
            Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_rotation_is_not_replayed_so_a_real_replay_still_reaches_reuse_detection()
    {
        ClearRotationWindow();
        _tokens.HashRefreshToken(Arg.Any<string>()).Returns(new byte[32]);
        _tokens.CreateRefreshToken().Returns(("new", new byte[32]));
        _writer.RedeemRefreshTokenAsync(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
               .Returns(Given.Failed<RefreshRedemption>(401, "Please sign in again."));

        var service = Service();
        var first  = await service.RefreshAsync("a-spent-token", null);
        var second = await service.RefreshAsync("a-spent-token", null);

        Assert.False(first.IsSuccess);
        Assert.False(second.IsSuccess);

        await _writer.Received(2).RedeemRefreshTokenAsync(
            Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detected_reuse_is_reported_as_a_401_and_issues_nothing()
    {
        ClearRotationWindow();
        _tokens.HashRefreshToken(Arg.Any<string>()).Returns(new byte[32]);
        _tokens.CreateRefreshToken().Returns(("new", new byte[32]));
        _writer.RedeemRefreshTokenAsync(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
               .Returns(Given.FailedWithData(
                   new RefreshRedemption { ReuseDetected = true, UserId = null }, 401, "Please sign in again."));

        var result = await Service().RefreshAsync("a-stolen-token", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.ResponseCode);
        Assert.Null(result.Value);
        _tokens.DidNotReceiveWithAnyArgs().CreateAccessToken(default!, default!);
    }

    [Fact]
    public async Task A_rotation_the_database_cannot_store_fails_rather_than_handing_out_a_token()
    {
        ClearRotationWindow();
        GivenRotationSucceeds();
        _writer.CreateRefreshTokenAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<Guid>(),
                                       Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
               .Returns(Given.Failed<long?>(500, "Could not start the session."));

        _tokens.ValidateTwoFactorChallengeToken(Arg.Any<string>()).Returns("user-1");
        GivenUser(AUserWithPassword());
        _reader.GetTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok<string?>(ProtectedSecret()));
        _twoFactor.VerifyCode(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var result = await Service().VerifyTwoFactorAsync(
            new TwoFactorRequest { MfaToken = "t", Code = "123456" }, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.ResponseCode);
    }

    // ── enrolment ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Beginning_enrolment_stores_the_secret_encrypted_and_returns_it_once()
    {
        GivenUser(Given.AUser());
        _twoFactor.GenerateSecret().Returns("JBSWY3DPEHPK3PXP");
        _twoFactor.BuildAuthenticatorUri(Arg.Any<string>(), Arg.Any<string>()).Returns("otpauth://totp/...");

        var result = await Service().BeginTwoFactorEnrolmentAsync("user-1");

        Assert.Equal("JBSWY3DPEHPK3PXP", result.Value!.SharedKey);

        await _writer.Received(1).SetTokenAsync(
            "user-1", TokenStore.Provider, TokenStore.AuthenticatorKey,
            // Whatever is stored, it is not the secret in the clear.
            Arg.Is<string?>(v => v != null && v != "JBSWY3DPEHPK3PXP"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirming_with_a_wrong_code_does_not_turn_two_factor_on()
    {
        _reader.GetTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok<string?>(ProtectedSecret()));
        _twoFactor.VerifyCode(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var result = await Service().ConfirmTwoFactorEnrolmentAsync(
            "user-1", new ConfirmTwoFactorRequest { Code = "000000" });

        Assert.Equal(401, result.ResponseCode);
        await _writer.DidNotReceive().UpdateUserAsync(
            Arg.Is<UserUpdate>(u => u.TwoFactorEnabled == true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirming_without_a_pending_key_asks_the_user_to_start_again()
    {
        _reader.GetTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok<string?>(null));

        var result = await Service().ConfirmTwoFactorEnrolmentAsync(
            "user-1", new ConfirmTwoFactorRequest { Code = "123456" });

        Assert.Equal(409, result.ResponseCode);
    }

    [Fact]
    public async Task Confirming_turns_two_factor_on_and_the_first_recovery_code_clears_any_old_set()
    {
        _reader.GetTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok<string?>(ProtectedSecret()));
        _twoFactor.VerifyCode(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _twoFactor.GenerateRecoveryCodes(Arg.Any<int>()).Returns(["one", "two", "three"]);
        _twoFactor.HashRecoveryCode(Arg.Any<string>()).Returns([1]);
        _writer.AddRecoveryCodeAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok(true));

        var result = await Service().ConfirmTwoFactorEnrolmentAsync(
            "user-1", new ConfirmTwoFactorRequest { Code = "123456" });

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.RecoveryCodes.Count);

        await _writer.Received(1).UpdateUserAsync(
            Arg.Is<UserUpdate>(u => u.TwoFactorEnabled == true), Arg.Any<CancellationToken>());

        // Exactly one write clears — an old printout must stop working.
        await _writer.Received(1).AddRecoveryCodeAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(), true, Arg.Any<CancellationToken>());
        await _writer.Received(2).AddRecoveryCodeAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_authenticator_key_that_cannot_be_decrypted_fails_closed()
    {
        _reader.GetTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Given.Ok<string?>("not-ciphertext-this-service-can-read"));

        var result = await Service().ConfirmTwoFactorEnrolmentAsync(
            "user-1", new ConfirmTwoFactorRequest { Code = "123456" });

        Assert.False(result.IsSuccess);
    }
}
