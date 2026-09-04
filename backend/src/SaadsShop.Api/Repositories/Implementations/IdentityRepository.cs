using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;

namespace SaadsShop.Api.Repositories.Implementations;

public sealed class IdentityRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), IIdentityRepository
{
    public Task<ProcedureResult<AppUser?>> FindUserAsync(
        string? userId = null, string? normalizedEmail = null, string? normalizedUserName = null,
        CancellationToken ct = default)
        => ExecuteAsync<AppUser?>(
            StoredProcedures.UserGet,
            new { UserId = userId, NormalizedEmail = normalizedEmail, NormalizedUserName = normalizedUserName },
            async grid =>
            {
                var user  = await grid.ReadSingleOrDefaultAsync<AppUser>();
                var roles = (await grid.ReadAsync<string>()).AsList();

                // The roles set must be read even when there is no user, or the
                // status row that follows would be consumed as roles.
                if (user is not null) user.Roles = roles;

                return user;
            },
            ct);

    public Task<ProcedureResult<bool>> CreateUserAsync(AppUser user, string roleName, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.UserCreate,
            new
            {
                user.Id,
                user.UserName,
                user.NormalizedUserName,
                user.Email,
                user.NormalizedEmail,
                user.EmailConfirmed,
                user.PasswordHash,
                user.SecurityStamp,
                user.ConcurrencyStamp,
                user.PhoneNumber,
                user.FullName,
                RoleName = roleName
            },
            ct);

    public Task<ProcedureResult<bool>> UpdateUserAsync(UserUpdate update, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.UserUpdate,
            new
            {
                update.Id,
                update.UserName,
                update.NormalizedUserName,
                update.Email,
                update.NormalizedEmail,
                update.EmailConfirmed,
                update.PasswordHash,
                update.SecurityStamp,
                update.ConcurrencyStamp,
                update.PhoneNumber,
                update.FullName,
                update.TwoFactorEnabled,
                update.LockoutEnd,
                update.ClearLockout,
                update.LockoutEnabled,
                update.AccessFailedCount,
                update.IsActive
            },
            ct);

    public Task<ProcedureResult<IReadOnlyList<StaffAccount>>> GetStaffAsync(CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<StaffAccount>>(
            StoredProcedures.StaffGetList,
            parameters: null,
            async grid => (await grid.ReadAsync<StaffAccount>()).AsList(),
            ct);

    public Task<ProcedureResult<bool>> SetRoleAsync(
        string userId, string roleName, bool attach, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.RoleSetForUser,
            new { UserId = userId, RoleName = roleName, Attach = attach },
            ct);

    public Task<ProcedureResult<bool>> AddExternalLoginAsync(
        string userId, string provider, string providerKey, string? displayName, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.UserLoginAdd,
            new
            {
                UserId              = userId,
                LoginProvider       = provider,
                ProviderKey         = providerKey,
                ProviderDisplayName = displayName
            },
            ct);

    public Task<ProcedureResult<bool>> RemoveExternalLoginAsync(
        string userId, string provider, string providerKey, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.UserLoginRemove,
            new { UserId = userId, LoginProvider = provider, ProviderKey = providerKey },
            ct);

    public Task<ProcedureResult<bool>> SetTokenAsync(
        string userId, string provider, string name, string? value, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.UserTokenSet,
            new { UserId = userId, LoginProvider = provider, Name = name, Value = value },
            ct);

    public Task<ProcedureResult<string?>> GetTokenAsync(
        string userId, string provider, string name, CancellationToken ct = default)
        => ExecuteAsync<string?>(
            StoredProcedures.UserTokenGet,
            new { UserId = userId, LoginProvider = provider, Name = name },
            async grid => await grid.ReadSingleOrDefaultAsync<string?>(),
            ct);

    public Task<ProcedureResult<bool>> RemoveTokenAsync(
        string userId, string provider, string name, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.UserTokenRemove,
            new { UserId = userId, LoginProvider = provider, Name = name },
            ct);

    public Task<ProcedureResult<long?>> CreateRefreshTokenAsync(
        string userId, byte[] tokenHash, Guid familyId, DateTime expiresAt, string? ip,
        CancellationToken ct = default)
        => ExecuteAsync<long?>(
            StoredProcedures.RefreshTokenCreate,
            new
            {
                UserId      = userId,
                TokenHash   = tokenHash,
                FamilyId    = familyId,
                ExpiresAt   = expiresAt,
                CreatedByIp = ip
            },
            async grid => (await grid.ReadSingleOrDefaultAsync<CreatedToken>())?.RefreshTokenId,
            ct);

    public Task<ProcedureResult<RefreshRedemption>> RedeemRefreshTokenAsync(
        byte[] presentedHash, byte[] newTokenHash, DateTime newExpiresAt, string? ip,
        CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.RefreshTokenRedeem,
            new
            {
                PresentedHash = presentedHash,
                NewTokenHash  = newTokenHash,
                NewExpiresAt  = newExpiresAt,
                CreatedByIp   = ip
            },
            async grid =>
            {
                var redemption = await grid.ReadSingleOrDefaultAsync<RefreshRedemption>()
                                 ?? new RefreshRedemption();
                redemption.Roles = (await grid.ReadAsync<string>()).AsList();
                return redemption;
            },
            ct);

    public Task<ProcedureResult<int>> RevokeRefreshTokensAsync(
        byte[]? tokenHash, string? userId, Guid? familyId, string reason, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.RefreshTokenRevoke,
            new { TokenHash = tokenHash, UserId = userId, FamilyId = familyId, Reason = reason },
            async grid => (await grid.ReadSingleOrDefaultAsync<RevokeResult>())?.RevokedCount ?? 0,
            ct);

    public Task<ProcedureResult<bool>> AddRecoveryCodeAsync(
        string userId, byte[] codeHash, bool clearExisting, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.RecoveryCodeAdd,
            new { UserId = userId, CodeHash = codeHash, ClearExisting = clearExisting },
            ct);

    public Task<ProcedureResult<int>> RedeemRecoveryCodeAsync(
        string userId, byte[] codeHash, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.RecoveryCodeRedeem,
            new { UserId = userId, CodeHash = codeHash },
            async grid => (await grid.ReadSingleOrDefaultAsync<RecoveryResult>())?.RemainingCodes ?? 0,
            ct);

    private sealed class CreatedToken  { public long? RefreshTokenId { get; set; } }
    private sealed class RevokeResult   { public int RevokedCount    { get; set; } }
    private sealed class RecoveryResult { public int RemainingCodes  { get; set; } }
}
