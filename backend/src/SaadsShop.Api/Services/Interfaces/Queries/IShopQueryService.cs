using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces.Queries;

/// <summary>Shop settings and the dashboard — read.</summary>
public interface IShopQueryService
{
    /// <summary>What the storefront may know: no flags, only the methods that are on.</summary>
    Task<OperationResult<ShopSettingsPublicResponse>> GetPublicSettingsAsync(CancellationToken ct = default);

    /// <summary>The full settings row, for the Owner's screen.</summary>
    Task<OperationResult<ShopSettingsResponse>> GetSettingsAsync(CancellationToken ct = default);

    Task<OperationResult<DashboardResponse>> GetDashboardAsync(
        DashboardQuery query, CancellationToken ct = default);
}
