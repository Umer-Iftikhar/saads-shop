using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces.Queries;

/// <summary>
/// Accounts, read — all through stored procedures, so the "no inline SQL" rule
/// holds for authentication too, which is where it matters most.
/// </summary>
public interface IIdentityQueryRepository
{
    Task<ProcedureResult<AppUser?>> FindUserAsync(
        string? userId = null, string? normalizedEmail = null, string? normalizedUserName = null,
        CancellationToken ct = default);

    Task<ProcedureResult<IReadOnlyList<StaffAccount>>> GetStaffAsync(CancellationToken ct = default);

    Task<ProcedureResult<string?>> GetTokenAsync(
        string userId, string provider, string name, CancellationToken ct = default);
}
