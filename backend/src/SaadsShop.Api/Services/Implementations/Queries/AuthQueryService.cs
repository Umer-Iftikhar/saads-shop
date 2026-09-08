using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Repositories.Interfaces.Queries;
using SaadsShop.Api.Services.Interfaces.Queries;

namespace SaadsShop.Api.Services.Implementations.Queries;

public sealed class AuthQueryService(IIdentityQueryRepository repository) : IAuthQueryService
{
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

    public async Task<OperationResult<IReadOnlyList<StaffAccountResponse>>> GetStaffAsync(CancellationToken ct = default)
    {
        var result = await repository.GetStaffAsync(ct);

        if (!result.IsSuccess || result.Data is null)
            return OperationResult<IReadOnlyList<StaffAccountResponse>>.FromProcedureFailure(result);

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
}
