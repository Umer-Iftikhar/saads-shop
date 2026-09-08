using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces.Queries;

/// <summary>
/// The two things authentication can be asked that change nothing.
/// </summary>
/// <remarks>
/// Almost everything about signing in is a write — a failed attempt is
/// recorded, a refresh token rotates, a recovery code is spent — so this
/// interface is deliberately small. That is the honest shape of it, not an
/// oversight.
/// </remarks>
public interface IAuthQueryService
{
    Task<OperationResult<CurrentUserResponse>> GetCurrentUserAsync(string userId, CancellationToken ct = default);

    Task<OperationResult<IReadOnlyList<StaffAccountResponse>>> GetStaffAsync(CancellationToken ct = default);
}
