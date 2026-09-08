using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces.Queries;

/// <summary>Shop settings and the dashboard — read.</summary>
public interface IShopQueryRepository
{
    /// <summary>
    /// The storefront's view of settings. A separate procedure from
    /// <see cref="GetSettingsAsync"/> rather than a filtered projection, so a
    /// mistake in a controller cannot leak a column the public never sees.
    /// </summary>
    Task<ProcedureResult<ShopSettings>> GetPublicSettingsAsync(CancellationToken ct = default);

    Task<ProcedureResult<ShopSettings>> GetSettingsAsync(CancellationToken ct = default);

    Task<ProcedureResult<DashboardData>> GetDashboardAsync(
        DateOnly? asAt, CancellationToken ct = default);
}
