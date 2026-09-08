using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;

namespace SaadsShop.Api.Repositories.Interfaces.Commands;

/// <summary>Shop settings — written.</summary>
public interface IShopCommandRepository
{
    Task<ProcedureResult<bool>> UpdateSettingsAsync(
        SettingsUpdateRequest request, string normalisedWhatsApp, string? actorUserId,
        CancellationToken ct = default);
}
