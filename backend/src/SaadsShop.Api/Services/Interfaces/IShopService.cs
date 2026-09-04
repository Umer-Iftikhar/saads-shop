using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces;

public interface IShopService
{
    Task<OperationResult<ShopSettingsPublicResponse>> GetPublicSettingsAsync(CancellationToken ct = default);
    Task<OperationResult<ShopSettingsResponse>>       GetSettingsAsync(CancellationToken ct = default);
    Task<OperationResult<bool>>                       UpdateSettingsAsync(SettingsUpdateRequest request, string? actorUserId, CancellationToken ct = default);
    Task<OperationResult<DashboardResponse>>          GetDashboardAsync(DashboardQuery query, CancellationToken ct = default);
}
