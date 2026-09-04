using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;

namespace SaadsShop.Api.Repositories.Implementations;

public sealed class ShopRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), IShopRepository
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

    public Task<ProcedureResult<bool>> UpdateSettingsAsync(
        SettingsUpdateRequest request, string normalisedWhatsApp, string? actorUserId,
        CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.SettingsUpdate,
            new
            {
                request.ShopName,
                request.City,
                request.AddressLine,
                WhatsAppNumber = normalisedWhatsApp,
                request.BannerText,
                request.OpeningHours,
                request.DeliveryCharge,
                request.FreeDeliveryThreshold,
                request.CashOnDeliveryEnabled,
                request.WhatsAppOrdersEnabled,
                request.ReserveInShopEnabled,
                request.CardPaymentEnabled,
                ActorUserId = actorUserId
            },
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
