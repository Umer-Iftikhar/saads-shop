using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces.Queries;

namespace SaadsShop.Api.Repositories.Implementations.Queries;

public sealed class IdentityQueryRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), IIdentityQueryRepository
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

    public Task<ProcedureResult<IReadOnlyList<StaffAccount>>> GetStaffAsync(CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<StaffAccount>>(
            StoredProcedures.StaffGetList,
            parameters: null,
            async grid => (await grid.ReadAsync<StaffAccount>()).AsList(),
            ct);

    public Task<ProcedureResult<string?>> GetTokenAsync(
        string userId, string provider, string name, CancellationToken ct = default)
        => ExecuteAsync<string?>(
            StoredProcedures.UserTokenGet,
            new { UserId = userId, LoginProvider = provider, Name = name },
            async grid => await grid.ReadSingleOrDefaultAsync<string?>(),
            ct);
}
