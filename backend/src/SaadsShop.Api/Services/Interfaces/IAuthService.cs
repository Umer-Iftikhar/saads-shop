using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces;

/// <summary>
/// Sign-in, two-factor, session rotation and staff accounts.
/// </summary>
/// <remarks>
/// Methods that issue a session return the refresh token separately from the
/// response body: the controller writes it to an HttpOnly cookie and it never
/// reaches JavaScript. See docs/security.md.
/// </remarks>
public interface IAuthService
{
    Task<OperationResult<LoginChallengeResponse>> LoginAsync(LoginRequest request, string? ip, CancellationToken ct = default);

    Task<OperationResult<SessionIssued>> VerifyTwoFactorAsync(TwoFactorRequest request, string? ip, CancellationToken ct = default);

    Task<OperationResult<SessionIssued>> RefreshAsync(string refreshToken, string? ip, CancellationToken ct = default);

    Task<OperationResult<bool>> LogoutAsync(string? refreshToken, string userId, CancellationToken ct = default);

    /// <summary>
    /// Completes a Google sign-in. Links by verified email only, and never
    /// provisions a new staff account — an Owner must invite first, or anyone
    /// with a Google account could give themselves a foothold.
    /// </summary>
    Task<OperationResult<LoginChallengeResponse>> ExternalLoginAsync(
        string provider, string providerKey, string email, bool emailVerified, string? displayName,
        string? ip, CancellationToken ct = default);

    Task<OperationResult<CurrentUserResponse>>    GetCurrentUserAsync(string userId, CancellationToken ct = default);
    Task<OperationResult<TwoFactorSetupResponse>> BeginTwoFactorEnrolmentAsync(string userId, CancellationToken ct = default);
    Task<OperationResult<RecoveryCodesResponse>>  ConfirmTwoFactorEnrolmentAsync(string userId, ConfirmTwoFactorRequest request, CancellationToken ct = default);

    Task<OperationResult<IReadOnlyList<StaffAccountResponse>>> GetStaffAsync(CancellationToken ct = default);
    Task<OperationResult<string>> CreateStaffAsync(CreateStaffRequest request, CancellationToken ct = default);
    Task<OperationResult<bool>>   SetRoleAsync(SetRoleRequest request, CancellationToken ct = default);
}

/// <summary>
/// A newly issued session. The refresh token is handed back once, for the
/// controller to place in a cookie; it is never serialised into a body.
/// </summary>
public sealed class SessionIssued
{
    public required AuthResponse Auth                  { get; init; }
    public required string       RefreshToken          { get; init; }
    public required DateTime     RefreshTokenExpiresAt { get; init; }
}
