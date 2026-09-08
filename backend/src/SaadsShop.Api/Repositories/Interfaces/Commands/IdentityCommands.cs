using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces.Commands;

/// <summary>
/// Accounts, roles, external logins, refresh tokens and 2FA material — written.
/// </summary>
/// <remarks>
/// Several of these are writes that a reader would not expect to be writes,
/// which is part of why the split earns its keep: signing in records a failed
/// attempt, refreshing rotates a token, and redeeming a recovery code spends
/// it. None of them belong on a read-only surface.
/// </remarks>
public interface IIdentityCommandRepository
{
    Task<ProcedureResult<bool>> CreateUserAsync(AppUser user, string roleName, CancellationToken ct = default);
    Task<ProcedureResult<bool>> UpdateUserAsync(UserUpdate update, CancellationToken ct = default);

    Task<ProcedureResult<bool>> SetRoleAsync(string userId, string roleName, bool attach, CancellationToken ct = default);

    Task<ProcedureResult<bool>> AddExternalLoginAsync(
        string userId, string provider, string providerKey, string? displayName, CancellationToken ct = default);
    Task<ProcedureResult<bool>> RemoveExternalLoginAsync(
        string userId, string provider, string providerKey, CancellationToken ct = default);

    Task<ProcedureResult<bool>> SetTokenAsync(string userId, string provider, string name, string? value, CancellationToken ct = default);
    Task<ProcedureResult<bool>> RemoveTokenAsync(string userId, string provider, string name, CancellationToken ct = default);

    Task<ProcedureResult<long?>> CreateRefreshTokenAsync(
        string userId, byte[] tokenHash, Guid familyId, DateTime expiresAt, string? ip,
        CancellationToken ct = default);

    /// <summary>
    /// Rotates a refresh token. Redeeming an already-spent token revokes the
    /// whole family and comes back with <c>ReuseDetected</c> set, so the API can
    /// log a security event rather than an ordinary failed refresh.
    /// </summary>
    Task<ProcedureResult<RefreshRedemption>> RedeemRefreshTokenAsync(
        byte[] presentedHash, byte[] newTokenHash, DateTime newExpiresAt, string? ip,
        CancellationToken ct = default);

    Task<ProcedureResult<int>> RevokeRefreshTokensAsync(
        byte[]? tokenHash, string? userId, Guid? familyId, string reason, CancellationToken ct = default);

    Task<ProcedureResult<bool>> AddRecoveryCodeAsync(
        string userId, byte[] codeHash, bool clearExisting, CancellationToken ct = default);

    Task<ProcedureResult<int>> RedeemRecoveryCodeAsync(
        string userId, byte[] codeHash, CancellationToken ct = default);
}
