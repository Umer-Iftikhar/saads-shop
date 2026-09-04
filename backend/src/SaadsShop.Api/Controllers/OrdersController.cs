using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Controllers;

/// <summary>
/// Customer-facing ordering. Anonymous: the shop takes cash on delivery,
/// WhatsApp orders and reserve-in-shop, none of which need an account.
/// </summary>
[Route("api/orders")]
public sealed class OrdersController(IOrderService orders) : ApiControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.PlaceOrder)]
    public async Task<IActionResult> Place([FromBody] PlaceOrderRequest request, CancellationToken ct)
    {
        var result = await orders.PlaceOrderAsync(request, ct);

        return result.IsSuccess
            ? CreatedFromResult(result, $"/api/orders/{result.Value!.Reference}")
            : Problem(result);
    }

    /// <summary>
    /// Looks up an order by reference plus the phone it was placed with.
    /// </summary>
    /// <remarks>
    /// The phone is the shared secret. References are sequential — SS-2419 sits
    /// next to SS-2418 — so one alone would let anyone walk the shop's orders.
    /// Rate-limited for the same reason.
    /// </remarks>
    [HttpGet("{reference}")]
    [EnableRateLimiting(RateLimitPolicies.TrackOrder)]
    public async Task<IActionResult> Track(string reference, [FromQuery] string phone, CancellationToken ct)
        => FromResult(await orders.TrackAsync(new TrackOrderQuery { Reference = reference, Phone = phone }, ct));
}

/// <summary>Prices a bistar + parde + cushion combination from the set builder.</summary>
[Route("api/set-builder")]
public sealed class SetBuilderController(IOrderService orders) : ApiControllerBase
{
    [HttpPost("quote")]
    public async Task<IActionResult> Quote([FromBody] SetBuilderQuoteRequest request, CancellationToken ct)
        => FromResult(await orders.QuoteSetAsync(request, ct));
}

/// <summary>The storefront's view of the shop: address, hours, delivery, payment methods.</summary>
[Route("api/shop")]
public sealed class ShopController(IShopService shop) : ApiControllerBase
{
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
        => FromResult(await shop.GetPublicSettingsAsync(ct));
}
