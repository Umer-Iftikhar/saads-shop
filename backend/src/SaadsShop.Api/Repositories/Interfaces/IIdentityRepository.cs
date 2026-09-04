using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces;

/// <summary>
/// Accounts, roles, external logins, refresh tokens and 2FA material — all
/// through stored procedures, so the "no inline SQL" rule holds for
/// authentication too, which is where it matters most.
/// </summary>
public interface IIdentityRepository
{
    Task<ProcedureResult<AppUser?>> FindUserAsync(
        string? userId = null, string? normalizedEmail = null, string? normalizedUserName = null,
        CancellationToken ct = default);

    Task<ProcedureResult<bool>> CreateUserAsync(AppUser user, string roleName, CancellationToken ct = default);
    Task<ProcedureResult<bool>> UpdateUserAsync(UserUpdate update, CancellationToken ct = default);

    Task<ProcedureResult<IReadOnlyList<StaffAccount>>> GetStaffAsync(CancellationToken ct = default);
    Task<ProcedureResult<bool>> SetRoleAsync(string userId, string roleName, bool attach, CancellationToken ct = default);

    Task<ProcedureResult<bool>> AddExternalLoginAsync(
        string userId, string provider, string providerKey, string? displayName, CancellationToken ct = default);
    Task<ProcedureResult<bool>> RemoveExternalLoginAsync(
        string userId, string provider, string providerKey, CancellationToken ct = default);

    Task<ProcedureResult<bool>>    SetTokenAsync(string userId, string provider, string name, string? value, CancellationToken ct = default);
    Task<ProcedureResult<string?>> GetTokenAsync(string userId, string provider, string name, CancellationToken ct = default);
    Task<ProcedureResult<bool>>    RemoveTokenAsync(string userId, string provider, string name, CancellationToken ct = default);

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

/// <summary>
/// A partial user update. Every field is nullable and null means "leave alone"
/// — Identity updates one facet at a time (a failed sign-in touches only the
/// failure count), and a full-row update would clobber concurrent changes.
/// </summary>
public sealed class UserUpdate
{
    public required string Id { get; init; }

    public string?  UserName           { get; init; }
    public string?  NormalizedUserName { get; init; }
    public string?  Email              { get; init; }
    public string?  NormalizedEmail    { get; init; }
    public bool?    EmailConfirmed     { get; init; }
    public string?  PasswordHash       { get; init; }
    public string?  SecurityStamp      { get; init; }
    public string?  ConcurrencyStamp   { get; init; }
    public string?  PhoneNumber        { get; init; }
    public string?  FullName           { get; init; }
    public bool?    TwoFactorEnabled   { get; init; }
    public DateTimeOffset? LockoutEnd  { get; init; }
    public bool     ClearLockout       { get; init; }
    public bool?    LockoutEnabled     { get; init; }
    public int?     AccessFailedCount  { get; init; }
    public bool?    IsActive           { get; init; }
}

public sealed class RefreshRedemption
{
    public string?  UserId         { get; set; }
    public string?  Email          { get; set; }
    public string?  FullName       { get; set; }
    public long?    RefreshTokenId { get; set; }
    public bool     ReuseDetected  { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
}
