using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;

namespace SaadsShop.Api.Services.Interfaces.Commands;

/// <summary>Changing how the shop presents itself and takes money.</summary>
public interface IShopCommandService
{
    Task<OperationResult<bool>> UpdateSettingsAsync(
        SettingsUpdateRequest request, string? actorUserId, CancellationToken ct = default);
}
