using SaadsShop.Api.Common;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Services.Implementations;

public sealed class ShopService(
    IShopRepository repository,
    ICacheService cache,
    ILogger<ShopService> logger) : IShopService
{
    public async Task<OperationResult<ShopSettingsPublicResponse>> GetPublicSettingsAsync(CancellationToken ct = default)
    {
        var key = CacheKeys.PublicSettings(cache.GetVersion(CacheKeys.SettingsVersion));

        return await cache.GetOrCreateAsync(key, CacheKeys.Lifetimes.Settings, async () =>
        {
            var result = await repository.GetPublicSettingsAsync(ct);

            if (!result.IsSuccess || result.Data is null)
                return OperationResult<ShopSettingsPublicResponse>
                    .Failure(result.ResponseCode, result.ResponseMessage);

            var s = result.Data;

            // Only the methods actually switched on. Sending the flags and
            // letting the client decide would let a stale page offer a payment
            // method the shop has turned off.
            var methods = new List<string>(4);
            if (s.CashOnDeliveryEnabled) methods.Add(nameof(PaymentMethod.CashOnDelivery));
            if (s.WhatsAppOrdersEnabled) methods.Add(nameof(PaymentMethod.WhatsApp));
            if (s.ReserveInShopEnabled)  methods.Add(nameof(PaymentMethod.ReserveInShop));
            if (s.CardPaymentEnabled)    methods.Add(nameof(PaymentMethod.Card));

            return OperationResult<ShopSettingsPublicResponse>.Success(new ShopSettingsPublicResponse
            {
                ShopName              = s.ShopName,
                City                  = s.City,
                AddressLine           = s.AddressLine,
                WhatsAppNumber        = s.WhatsAppNumber,
                BannerText            = s.BannerText,
                OpeningHours          = s.OpeningHours,
                DeliveryCharge        = s.DeliveryCharge,
                FreeDeliveryThreshold = s.FreeDeliveryThreshold,
                PaymentMethods        = methods
            });
        });
    }

    public async Task<OperationResult<ShopSettingsResponse>> GetSettingsAsync(CancellationToken ct = default)
    {
        var result = await repository.GetSettingsAsync(ct);

        if (!result.IsSuccess || result.Data is null)
            return OperationResult<ShopSettingsResponse>.Failure(result.ResponseCode, result.ResponseMessage);

        var s = result.Data;

        return OperationResult<ShopSettingsResponse>.Success(new ShopSettingsResponse
        {
            ShopName              = s.ShopName,
            City                  = s.City,
            AddressLine           = s.AddressLine,
            WhatsAppNumber        = s.WhatsAppNumber,
            BannerText            = s.BannerText,
            OpeningHours          = s.OpeningHours,
            DeliveryCharge        = s.DeliveryCharge,
            FreeDeliveryThreshold = s.FreeDeliveryThreshold,
            CashOnDeliveryEnabled = s.CashOnDeliveryEnabled,
            WhatsAppOrdersEnabled = s.WhatsAppOrdersEnabled,
            ReserveInShopEnabled  = s.ReserveInShopEnabled,
            CardPaymentEnabled    = s.CardPaymentEnabled,
            UpdatedAt             = s.UpdatedAt,
            UpdatedBy             = s.UpdatedBy
        });
    }

    public async Task<OperationResult<bool>> UpdateSettingsAsync(
        SettingsUpdateRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var whatsApp = PhoneNumber.Normalise(request.WhatsAppNumber);

        if (whatsApp is null)
        {
            return OperationResult<bool>.Invalid(new Dictionary<string, string[]>
            {
                [nameof(SettingsUpdateRequest.WhatsAppNumber)] =
                    ["The WhatsApp number should look like 03xx xxx xxxx."]
            });
        }

        var result = await repository.UpdateSettingsAsync(request, whatsApp, actorUserId, ct);

        if (!result.IsSuccess)
            return OperationResult<bool>.Failure(result.ResponseCode, result.ResponseMessage);

        cache.BumpVersion(CacheKeys.SettingsVersion);
        logger.LogInformation("Shop settings updated by {ActorUserId}", actorUserId);

        return OperationResult<bool>.Success(true, result.ResponseMessage);
    }

    public async Task<OperationResult<DashboardResponse>> GetDashboardAsync(
        DashboardQuery query, CancellationToken ct = default)
    {
        var day = query.AsAt ?? DateOnly.FromDateTime(DateTime.UtcNow);

        return await cache.GetOrCreateAsync(CacheKeys.Dashboard(day), CacheKeys.Lifetimes.Dashboard, async () =>
        {
            var result = await repository.GetDashboardAsync(query.AsAt, ct);

            if (!result.IsSuccess || result.Data is null)
                return OperationResult<DashboardResponse>.Failure(result.ResponseCode, result.ResponseMessage);

            var d = result.Data;

            return OperationResult<DashboardResponse>.Success(new DashboardResponse
            {
                Stats = new DashboardStatsResponse
                {
                    SalesToday                 = d.Stats.SalesToday,
                    SalesSameDayLastWeek       = d.Stats.SalesSameDayLastWeek,
                    // Null rather than a division by zero. "+∞%" against a week
                    // with no sales is not a number anyone can act on.
                    SalesChangePercent         = d.Stats.SalesSameDayLastWeek == 0
                        ? null
                        : Math.Round((d.Stats.SalesToday - d.Stats.SalesSameDayLastWeek)
                                     / d.Stats.SalesSameDayLastWeek * 100m, 1),
                    OrdersOpen                 = d.Stats.OrdersOpen,
                    OrdersAwaitingMeasurements = d.Stats.OrdersAwaitingMeasurements,
                    JobsOnFloor                = d.Stats.JobsOnFloor,
                    JobsDueTomorrow            = d.Stats.JobsDueTomorrow,
                    MonthToDateSales           = d.Stats.MonthToDateSales
                },
                SalesChart = d.SalesChart.Select(w => new SalesWeekResponse
                {
                    WeekStart  = DateOnly.FromDateTime(w.WeekStart),
                    Sales      = w.Sales,
                    OrderCount = w.OrderCount
                }).ToList(),
                BestSellers = d.BestSellers.Select(b => new BestSellerResponse
                {
                    ProductId        = b.ProductId,
                    Name             = b.Name,
                    SoldCount        = b.SoldCount,
                    Revenue          = b.Revenue,
                    SwatchColorValue = b.SwatchColorValue,
                    SwatchWeave      = b.SwatchWeave
                }).ToList(),
                LatestOrders = d.LatestOrders.Select(o => new OrderSummaryResponse
                {
                    OrderId       = o.OrderId,
                    Reference     = o.Reference,
                    PlacedAt      = o.PlacedAt,
                    CustomerName  = o.CustomerName,
                    ItemSummary   = o.ItemSummary,
                    Total         = o.Total,
                    PaymentMethod = o.PaymentMethod,
                    Status        = o.Status
                }).ToList()
            });
        });
    }
}
