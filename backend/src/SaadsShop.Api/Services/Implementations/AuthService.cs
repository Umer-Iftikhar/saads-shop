using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SaadsShop.Api.Common;
using SaadsShop.Api.Configuration;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Services.Implementations;

public sealed class AuthService(
    IIdentityRepository repository,
    ITokenService tokens,
    ITwoFactorService twoFactor,
    IPasswordHasher<AppUser> passwordHasher,
    IDataProtectionProvider dataProtection,
    IOptions<JwtOptions> jwtOptions,
    IOptions<AuthOptions> authOptions,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtOptions  _jwt  = jwtOptions.Value;
    private readonly AuthOptions _auth = authOptions.Value;

    /// <summary>
    /// Protects the TOTP secret at rest. The database holds ciphertext, so a
    /// leaked backup does not hand over everyone's second factor.
    /// </summary>
    private readonly IDataProtector _protector = dataProtection.CreateProtector("SaadsShop.Totp.v1");

    // ── sign in ──────────────────────────────────────────────────────────────

    public async Task<OperationResult<LoginChallengeResponse>> LoginAsync(
        LoginRequest request, string? ip, CancellationToken ct = default)
    {
        var normalisedEmail = request.Email.Trim().ToUpperInvariant();
        var found = await repository.FindUserAsync(normalizedEmail: normalisedEmail, ct: ct);
        var user  = found.Data;

        // Every failure below returns the same message. Distinguishing "no such
        // account" from "wrong password" turns sign-in into a way to discover
        // which staff emails exist.
        if (user is null || !user.IsActive || user.PasswordHash is null)
        {
            // Hash anyway so a missing account does not answer measurably
            // faster than a wrong password.
            passwordHasher.VerifyHashedPassword(new AppUser(), DummyHash, request.Password);

            logger.LogWarning("Failed sign-in for {Email} from {Ip}: no usable account", normalisedEmail, ip);
            return InvalidCredentials();
        }

        if (IsLockedOut(user))
        {
            logger.LogWarning("Sign-in attempt on locked account {UserId} from {Ip}", user.Id, ip);
            return OperationResult<LoginChallengeResponse>.Failure(
                ResponseCodes.Unauthorised,
                "Too many attempts. Please try again in a few minutes.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            await RecordFailedAttemptAsync(user, ip, ct);
            return InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // Identity raised its work factor since this hash was written.
            // Upgrade it now, while the plaintext is legitimately in hand.
            await repository.UpdateUserAsync(new UserUpdate
            {
                Id           = user.Id,
                PasswordHash = passwordHasher.HashPassword(user, request.Password)
            }, ct);
        }

        if (user.AccessFailedCount > 0)
            await repository.UpdateUserAsync(
                new UserUpdate { Id = user.Id, AccessFailedCount = 0, ClearLockout = true }, ct);

        var (mfaToken, _) = tokens.CreateTwoFactorChallengeToken(user);

        logger.LogInformation("Password step passed for {UserId} from {Ip}", user.Id, ip);

        // Never an access token here: every staff account carries 2FA, so the
        // password alone is one half of a sign-in, not a sign-in.
        return OperationResult<LoginChallengeResponse>.Success(new LoginChallengeResponse
        {
            RequiresTwoFactor   = true,
            MfaToken            = mfaToken,
            IsTwoFactorEnrolled = user.TwoFactorEnabled
        });
    }

    public async Task<OperationResult<SessionIssued>> VerifyTwoFactorAsync(
        TwoFactorRequest request, string? ip, CancellationToken ct = default)
    {
        var userId = tokens.ValidateTwoFactorChallengeToken(request.MfaToken);

        if (userId is null)
            return OperationResult<SessionIssued>.Failure(
                ResponseCodes.Unauthorised, "That sign-in attempt has expired. Please sign in again.");

        var found = await repository.FindUserAsync(userId: userId, ct: ct);
        var user  = found.Data;

        if (user is null || !user.IsActive)
            return OperationResult<SessionIssued>.Failure(
                ResponseCodes.Unauthorised, "That sign-in attempt has expired. Please sign in again.");

        var methods = new List<string> { AuthMethods.Password };

        if (request.IsRecoveryCode)
        {
            var hash   = twoFactor.HashRecoveryCode(request.Code);
            var result = await repository.RedeemRecoveryCodeAsync(user.Id, hash, ct);

            if (!result.IsSuccess)
            {
                await RecordFailedAttemptAsync(user, ip, ct);
                logger.LogWarning("Recovery code rejected for {UserId} from {Ip}", user.Id, ip);
                return OperationResult<SessionIssued>.Failure(ResponseCodes.Unauthorised, "That code is not valid.");
            }

            methods.Add(AuthMethods.RecoveryCode);
            methods.Add(AuthMethods.TwoFactor);

            logger.LogWarning(
                "Recovery code used by {UserId} from {Ip}; {Remaining} remaining",
                user.Id, ip, result.Data);
        }
        else
        {
            var secret = await GetTotpSecretAsync(user.Id, ct);

            if (secret is null)
                return OperationResult<SessionIssued>.Failure(
                    ResponseCodes.Unauthorised, "Two-factor is not set up on this account yet.");

            if (!twoFactor.VerifyCode(secret, request.Code))
            {
                await RecordFailedAttemptAsync(user, ip, ct);
                logger.LogWarning("TOTP rejected for {UserId} from {Ip}", user.Id, ip);
                return OperationResult<SessionIssued>.Failure(ResponseCodes.Unauthorised, "That code is not valid.");
            }

            methods.Add(AuthMethods.TwoFactor);
        }

        if (user.AccessFailedCount > 0)
            await repository.UpdateUserAsync(
                new UserUpdate { Id = user.Id, AccessFailedCount = 0, ClearLockout = true }, ct);

        logger.LogInformation("Sign-in complete for {UserId} from {Ip}", user.Id, ip);

        return await IssueSessionAsync(user, methods, familyId: Guid.NewGuid(), ip, ct);
    }

    // ── rotation ─────────────────────────────────────────────────────────────

    public async Task<OperationResult<SessionIssued>> RefreshAsync(
        string refreshToken, string? ip, CancellationToken ct = default)
    {
        var presented = tokens.HashRefreshToken(refreshToken);
        var (newToken, newHash) = tokens.CreateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

        // Rotation and reuse detection happen inside the procedure, under a row
        // lock, so two refreshes racing on the same token cannot both win.
        var result = await repository.RedeemRefreshTokenAsync(presented, newHash, expiresAt, ip, ct);

        if (result.Data?.ReuseDetected == true)
        {
            // A spent token was presented again: the lineage leaked. The
            // procedure has already revoked the whole family. Logged at Warning
            // because this is a security event, not a routine failure.
            logger.LogWarning(
                "Refresh token reuse detected from {Ip}; token family revoked. " +
                "Both the holder and the legitimate user must sign in again.", ip);
        }

        if (!result.IsSuccess || result.Data?.UserId is null)
            return OperationResult<SessionIssued>.Failure(
                ResponseCodes.Unauthorised,
                string.IsNullOrWhiteSpace(result.ResponseMessage) ? "Please sign in again." : result.ResponseMessage);

        var user = new AppUser
        {
            Id       = result.Data.UserId,
            Email    = result.Data.Email ?? string.Empty,
            FullName = result.Data.FullName ?? string.Empty,
            Roles    = result.Data.Roles
        };

        // The refreshed session inherits pwd+mfa: it descends from a sign-in
        // that performed both, and forcing 2FA again every fifteen minutes
        // would make the shop unusable.
        var (accessToken, accessExpires) =
            tokens.CreateAccessToken(user, [AuthMethods.Password, AuthMethods.TwoFactor]);

        return OperationResult<SessionIssued>.Success(new SessionIssued
        {
            Auth = new AuthResponse
            {
                AccessToken = accessToken,
                ExpiresAt   = accessExpires,
                UserId      = user.Id,
                Email       = user.Email,
                FullName    = user.FullName,
                Roles       = user.Roles
            },
            RefreshToken          = newToken,
            RefreshTokenExpiresAt = expiresAt
        });
    }

    public async Task<OperationResult<bool>> LogoutAsync(
        string? refreshToken, string userId, CancellationToken ct = default)
    {
        // Revoking by token revokes its whole family, so signing out on one
        // device does not leave a rotated sibling alive.
        var hash = string.IsNullOrWhiteSpace(refreshToken) ? null : tokens.HashRefreshToken(refreshToken);

        await repository.RevokeRefreshTokensAsync(
            hash, hash is null ? userId : null, null, "Signed out", ct);

        logger.LogInformation("User {UserId} signed out", userId);
        return OperationResult<bool>.Success(true, "Signed out.");
    }

    // ── external ─────────────────────────────────────────────────────────────

    public async Task<OperationResult<LoginChallengeResponse>> ExternalLoginAsync(
        string provider, string providerKey, string email, bool emailVerified, string? displayName,
        string? ip, CancellationToken ct = default)
    {
        // An unverified Google email proves nothing about who controls the
        // address, and linking on it would let anyone claim a staff account by
        // registering a Google account with that address.
        if (!emailVerified)
        {
            logger.LogWarning("External sign-in refused: {Provider} did not verify {Email}", provider, email);
            return OperationResult<LoginChallengeResponse>.Failure(
                ResponseCodes.Unauthorised, "Your Google account's email is not verified.");
        }

        var normalisedEmail = email.Trim().ToUpperInvariant();
        var found = await repository.FindUserAsync(normalizedEmail: normalisedEmail, ct: ct);
        var user  = found.Data;

        // No auto-provisioning, on purpose. If a first-time external sign-in
        // created an account, anyone with a Google account could give
        // themselves a foothold in the shop panel. An Owner invites first.
        if (user is null || !user.IsActive)
        {
            logger.LogWarning("External sign-in refused for unknown or inactive account {Email}", normalisedEmail);
            return OperationResult<LoginChallengeResponse>.Failure(
                ResponseCodes.Forbidden,
                "That Google account is not linked to a shop account. Ask the owner to add you first.");
        }

        await repository.AddExternalLoginAsync(user.Id, provider, providerKey, displayName, ct);

        var (mfaToken, _) = tokens.CreateTwoFactorChallengeToken(user);

        logger.LogInformation("Google sign-in passed for {UserId} from {Ip}", user.Id, ip);

        // Still only half a sign-in: Google proves the identity, 2FA proves
        // possession, and the shop requires both.
        return OperationResult<LoginChallengeResponse>.Success(new LoginChallengeResponse
        {
            RequiresTwoFactor   = true,
            MfaToken            = mfaToken,
            IsTwoFactorEnrolled = user.TwoFactorEnabled
        });
    }

    // ── enrolment and accounts ───────────────────────────────────────────────

    public async Task<OperationResult<CurrentUserResponse>> GetCurrentUserAsync(
        string userId, CancellationToken ct = default)
    {
        var found = await repository.FindUserAsync(userId: userId, ct: ct);

        if (found.Data is null)
            return OperationResult<CurrentUserResponse>.Failure(ResponseCodes.NotFound, "Account not found.");

        var u = found.Data;

        return OperationResult<CurrentUserResponse>.Success(new CurrentUserResponse
        {
            UserId           = u.Id,
            Email            = u.Email,
            FullName         = u.FullName,
            PhoneNumber      = u.PhoneNumber,
            TwoFactorEnabled = u.TwoFactorEnabled,
            Roles            = u.Roles
        });
    }

    public async Task<OperationResult<TwoFactorSetupResponse>> BeginTwoFactorEnrolmentAsync(
        string userId, CancellationToken ct = default)
    {
        var found = await repository.FindUserAsync(userId: userId, ct: ct);

        if (found.Data is null)
            return OperationResult<TwoFactorSetupResponse>.Failure(ResponseCodes.NotFound, "Account not found.");

        var secret = twoFactor.GenerateSecret();

        // Stored encrypted, and returned in the clear exactly once — there is
        // no endpoint that reads it back, because one would turn a hijacked
        // session into a permanent bypass of the second factor.
        await repository.SetTokenAsync(
            userId, TokenStore.Provider, TokenStore.AuthenticatorKey, _protector.Protect(secret), ct);

        return OperationResult<TwoFactorSetupResponse>.Success(new TwoFactorSetupResponse
        {
            SharedKey        = secret,
            AuthenticatorUri = twoFactor.BuildAuthenticatorUri(found.Data.Email, secret)
        });
    }

    public async Task<OperationResult<RecoveryCodesResponse>> ConfirmTwoFactorEnrolmentAsync(
        string userId, ConfirmTwoFactorRequest request, CancellationToken ct = default)
    {
        var secret = await GetTotpSecretAsync(userId, ct);

        if (secret is null)
            return OperationResult<RecoveryCodesResponse>.Failure(
                ResponseCodes.Conflict, "Start the setup again — no authenticator key is pending.");

        if (!twoFactor.VerifyCode(secret, request.Code))
            return OperationResult<RecoveryCodesResponse>.Failure(
                ResponseCodes.Unauthorised, "That code is not valid. Check the time on your phone and try again.");

        await repository.UpdateUserAsync(new UserUpdate { Id = userId, TwoFactorEnabled = true }, ct);

        var codes = twoFactor.GenerateRecoveryCodes();

        // The first write clears any previous set: regenerating must invalidate
        // the old codes, or an old printout still opens the door.
        var first = true;
        foreach (var code in codes)
        {
            await repository.AddRecoveryCodeAsync(userId, twoFactor.HashRecoveryCode(code), first, ct);
            first = false;
        }

        logger.LogInformation("Two-factor enrolled for {UserId}", userId);

        return OperationResult<RecoveryCodesResponse>.Success(
            new RecoveryCodesResponse { RecoveryCodes = codes },
            "Two-factor is on. Save these recovery codes — they are shown once.");
    }

    public async Task<OperationResult<IReadOnlyList<StaffAccountResponse>>> GetStaffAsync(CancellationToken ct = default)
    {
        var result = await repository.GetStaffAsync(ct);

        if (!result.IsSuccess || result.Data is null)
            return OperationResult<IReadOnlyList<StaffAccountResponse>>
                .Failure(result.ResponseCode, result.ResponseMessage);

        var now = DateTimeOffset.UtcNow;

        return OperationResult<IReadOnlyList<StaffAccountResponse>>.Success(
            result.Data.Select(s => new StaffAccountResponse
            {
                Id                 = s.Id,
                FullName           = s.FullName,
                Email              = s.Email,
                PhoneNumber        = s.PhoneNumber,
                TwoFactorEnabled   = s.TwoFactorEnabled,
                IsActive           = s.IsActive,
                IsLockedOut        = s.LockoutEnd is { } end && end > now,
                CreatedAt          = s.CreatedAt,
                Roles              = string.IsNullOrWhiteSpace(s.Roles)
                                        ? []
                                        : s.Roles.Split(", ", StringSplitOptions.RemoveEmptyEntries),
                ExternalLoginCount = s.ExternalLoginCount
            }).ToList());
    }

    public async Task<OperationResult<string>> CreateStaffAsync(
        CreateStaffRequest request, CancellationToken ct = default)
    {
        var phone = request.PhoneNumber is null ? null : PhoneNumber.Normalise(request.PhoneNumber);

        if (request.PhoneNumber is not null && phone is null)
        {
            return OperationResult<string>.Invalid(new Dictionary<string, string[]>
            {
                [nameof(CreateStaffRequest.PhoneNumber)] = ["That phone number does not look right."]
            });
        }

        var user = new AppUser
        {
            Id                 = Guid.NewGuid().ToString("N"),
            UserName           = request.Email.Trim(),
            NormalizedUserName = request.Email.Trim().ToUpperInvariant(),
            Email              = request.Email.Trim(),
            NormalizedEmail    = request.Email.Trim().ToUpperInvariant(),
            // An owner creating the account is the verification; there is no
            // public sign-up path that would need an email round trip.
            EmailConfirmed     = true,
            SecurityStamp      = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp   = Guid.NewGuid().ToString("N"),
            PhoneNumber        = phone,
            FullName           = request.FullName.Trim()
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        var result = await repository.CreateUserAsync(user, request.Role, ct);

        if (!result.IsSuccess)
            return OperationResult<string>.Failure(result.ResponseCode, result.ResponseMessage);

        logger.LogInformation("Staff account {UserId} created with role {Role}", user.Id, request.Role);

        return OperationResult<string>.Success(user.Id, result.ResponseMessage);
    }

    public async Task<OperationResult<bool>> SetRoleAsync(SetRoleRequest request, CancellationToken ct = default)
    {
        var result = await repository.SetRoleAsync(request.UserId, request.Role, request.Attach, ct);

        return result.IsSuccess
            ? OperationResult<bool>.Success(true, result.ResponseMessage)
            : OperationResult<bool>.Failure(result.ResponseCode, result.ResponseMessage);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A real PBKDF2 hash of a throwaway password, used to keep the timing of a
    /// missing account close to that of a wrong password. Computed once.
    /// </summary>
    private static readonly string DummyHash =
        new PasswordHasher<AppUser>().HashPassword(new AppUser(), "not-a-real-password-0000");

    private static OperationResult<LoginChallengeResponse> InvalidCredentials()
        => OperationResult<LoginChallengeResponse>.Failure(
            ResponseCodes.Unauthorised, "That email and password do not match.");

    private bool IsLockedOut(AppUser user)
        => user.LockoutEnabled && user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;

    private async Task RecordFailedAttemptAsync(AppUser user, string? ip, CancellationToken ct)
    {
        var failures = user.AccessFailedCount + 1;
        DateTimeOffset? lockoutEnd = null;

        if (user.LockoutEnabled && failures >= _auth.MaxFailedAccessAttempts)
        {
            lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(_auth.LockoutMinutes);
            logger.LogWarning(
                "Account {UserId} locked until {LockoutEnd} after {Failures} failed attempts from {Ip}",
                user.Id, lockoutEnd, failures, ip);
        }

        await repository.UpdateUserAsync(new UserUpdate
        {
            Id                = user.Id,
            AccessFailedCount = failures,
            LockoutEnd        = lockoutEnd
        }, ct);
    }

    private async Task<string?> GetTotpSecretAsync(string userId, CancellationToken ct)
    {
        var stored = await repository.GetTokenAsync(
            userId, TokenStore.Provider, TokenStore.AuthenticatorKey, ct);

        if (string.IsNullOrWhiteSpace(stored.Data)) return null;

        try
        {
            return _protector.Unprotect(stored.Data);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // The data-protection keyring has rotated or been lost, so this
            // ciphertext can no longer be read. Fail closed and log it — the
            // owner will need to re-enrol the account.
            logger.LogError("Could not decrypt the authenticator key for {UserId}; re-enrolment needed", userId);
            return null;
        }
    }

    private async Task<OperationResult<SessionIssued>> IssueSessionAsync(
        AppUser user, IEnumerable<string> methods, Guid familyId, string? ip, CancellationToken ct)
    {
        var (accessToken, accessExpires) = tokens.CreateAccessToken(user, methods);
        var (refreshToken, refreshHash)  = tokens.CreateRefreshToken();
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

        var stored = await repository.CreateRefreshTokenAsync(
            user.Id, refreshHash, familyId, refreshExpires, ip, ct);

        if (!stored.IsSuccess)
            return OperationResult<SessionIssued>.Failure(stored.ResponseCode, stored.ResponseMessage);

        return OperationResult<SessionIssued>.Success(new SessionIssued
        {
            Auth = new AuthResponse
            {
                AccessToken = accessToken,
                ExpiresAt   = accessExpires,
                UserId      = user.Id,
                Email       = user.Email,
                FullName    = user.FullName,
                Roles       = user.Roles
            },
            RefreshToken          = refreshToken,
            RefreshTokenExpiresAt = refreshExpires
        });
    }
}
