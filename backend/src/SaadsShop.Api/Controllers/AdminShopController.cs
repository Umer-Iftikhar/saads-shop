using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Controllers;

/// <summary>The overview screen.</summary>
[Route("api/admin/dashboard")]
[Authorize(Policy = AuthPolicies.StaffOnly)]
public sealed class AdminDashboardController(IShopService shop) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DashboardQuery query, CancellationToken ct)
        => FromResult(await shop.GetDashboardAsync(query, ct));
}

/// <summary>
/// Shop details, payment toggles and the delivery charge.
/// </summary>
/// <remarks>
/// Owner-only AND MfaVerified: this screen decides how the shop takes money, so
/// it demands proof that two-factor was actually performed for this session
/// rather than trusting a flag on the user record.
/// </remarks>
[Route("api/admin/settings")]
[Authorize(Policy = AuthPolicies.OwnerOnly)]
public sealed class AdminSettingsController(IShopService shop) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => FromResult(await shop.GetSettingsAsync(ct));

    [HttpPut]
    [Authorize(Policy = AuthPolicies.MfaVerified)]
    public async Task<IActionResult> Update([FromBody] SettingsUpdateRequest request, CancellationToken ct)
        => FromResult(await shop.UpdateSettingsAsync(request, CurrentUserId, ct),
                      StatusCodes.Status204NoContent);
}
