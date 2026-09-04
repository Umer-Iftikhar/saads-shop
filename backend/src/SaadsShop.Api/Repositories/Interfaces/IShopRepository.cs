using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces;

public interface IShopRepository
{
    /// <summary>
    /// The storefront's view of settings. A separate procedure from
    /// <see cref="GetSettingsAsync"/> rather than a filtered projection, so a
    /// mistake in a controller cannot leak a column the public never sees.
    /// </summary>
    Task<ProcedureResult<ShopSettings>> GetPublicSettingsAsync(CancellationToken ct = default);

    Task<ProcedureResult<ShopSettings>> GetSettingsAsync(CancellationToken ct = default);

    Task<ProcedureResult<bool>> UpdateSettingsAsync(
        SettingsUpdateRequest request, string normalisedWhatsApp, string? actorUserId,
        CancellationToken ct = default);

    Task<ProcedureResult<DashboardData>> GetDashboardAsync(
        DateOnly? asAt, CancellationToken ct = default);
}

public sealed class DashboardData
{
    public DashboardStats             Stats        { get; init; } = new();
    public IReadOnlyList<SalesWeek>   SalesChart   { get; init; } = [];
    public IReadOnlyList<BestSeller>  BestSellers  { get; init; } = [];
    public IReadOnlyList<Order>       LatestOrders { get; init; } = [];
}
