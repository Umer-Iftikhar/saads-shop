using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Controllers;

/// <summary>Orders as the shop panel sees them.</summary>
[Route("api/admin/orders")]
[Authorize(Policy = AuthPolicies.StaffOnly)]
public sealed class AdminOrdersController(IOrderService orders) : ApiControllerBase
{
    /// <summary>
    /// Search and filter. The date range is checked by [DateRange] on the query
    /// before the action runs, and again by the stored procedure.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] OrderSearchQuery query, CancellationToken ct)
        => FromResult(await orders.SearchAsync(query, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => FromResult(await orders.GetAsync(id, ct));

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(
        int id, [FromBody] UpdateOrderStatusRequest request, CancellationToken ct)
        => FromResult(await orders.UpdateStatusAsync(id, request, CurrentUserId, ct),
                      StatusCodes.Status204NoContent);

    [HttpPost("{id:int}/measurements")]
    public async Task<IActionResult> SaveMeasurements(
        int id, [FromBody] SaveMeasurementsRequest request, CancellationToken ct)
        => FromResult(await orders.SaveMeasurementsAsync(id, request, CurrentUserId, ct),
                      StatusCodes.Status204NoContent);
}
