using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Repositories.Interfaces.Queries;

namespace SaadsShop.Api.Repositories.Implementations.Queries;

public sealed class ShopQueryRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), IShopQueryRepository
{
    public Task<ProcedureResult<ShopSettings>> GetPublicSettingsAsync(CancellationToken ct = default)
        => ExecuteAsync<ShopSettings>(
            StoredProcedures.SettingsGetPublic,
            parameters: null,
            async grid => (await grid.ReadSingleOrDefaultAsync<ShopSettings>())!,
            ct);

    public Task<ProcedureResult<ShopSettings>> GetSettingsAsync(CancellationToken ct = default)
        => ExecuteAsync<ShopSettings>(
            StoredProcedures.SettingsGet,
            parameters: null,
            async grid => (await grid.ReadSingleOrDefaultAsync<ShopSettings>())!,
            ct);

    public Task<ProcedureResult<DashboardData>> GetDashboardAsync(
        DateOnly? asAt, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.DashboardGet,
            new { Today = asAt },
            async grid => new DashboardData
            {
                Stats        = await grid.ReadSingleAsync<DashboardStats>(),
                SalesChart   = (await grid.ReadAsync<SalesWeek>()).AsList(),
                BestSellers  = (await grid.ReadAsync<BestSeller>()).AsList(),
                LatestOrders = (await grid.ReadAsync<Order>()).AsList()
            },
            ct);
}
