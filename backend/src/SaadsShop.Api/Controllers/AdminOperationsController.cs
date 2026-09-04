using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Controllers;

/// <summary>Inventory levels and stock movements.</summary>
[Route("api/admin/inventory")]
[Authorize(Policy = AuthPolicies.StaffOnly)]
public sealed class AdminInventoryController(IOperationsService operations) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] InventorySearchQuery query, CancellationToken ct)
        => FromResult(await operations.GetInventoryAsync(query, ct));

    /// <summary>
    /// Signed adjustment with a reason. Every movement is written to the audit
    /// table, so "where did four sheets go" always has an answer.
    /// </summary>
    [HttpPost("{productId:int}/adjust")]
    public async Task<IActionResult> Adjust(
        int productId, [FromBody] AdjustStockRequest request, CancellationToken ct)
        => FromResult(await operations.AdjustStockAsync(productId, request, CurrentUserId, ct));
}

/// <summary>The stitching floor's board.</summary>
[Route("api/admin/stitching-queue")]
[Authorize(Policy = AuthPolicies.StaffOnly)]
public sealed class AdminStitchingController(IOperationsService operations) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => FromResult(await operations.GetStitchingBoardAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] StitchingJobCreateRequest request, CancellationToken ct)
        => FromResult(await operations.CreateStitchingJobAsync(request, ct), StatusCodes.Status201Created);

    [HttpPatch("{jobId:int}")]
    public async Task<IActionResult> Update(
        int jobId, [FromBody] StitchingJobUpdateRequest request, CancellationToken ct)
        => FromResult(await operations.UpdateStitchingJobAsync(jobId, request, ct),
                      StatusCodes.Status204NoContent);
}

/// <summary>Repeat buyers and the areas they order from.</summary>
[Route("api/admin/customers")]
[Authorize(Policy = AuthPolicies.StaffOnly)]
public sealed class AdminCustomersController(IOperationsService operations) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] CustomerSearchQuery query, CancellationToken ct)
        => FromResult(await operations.SearchCustomersAsync(query, ct));
}
