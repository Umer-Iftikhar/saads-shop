using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Services.Interfaces.Commands;
using SaadsShop.Api.Services.Interfaces.Queries;

namespace SaadsShop.Api.Controllers;

/// <summary>The product editor.</summary>
[Route("api/admin/products")]
[Authorize(Policy = AuthPolicies.StaffOnly)]
public sealed class AdminCatalogController(
    ICatalogQueryService reads,
    ICatalogCommandService writes) : ApiControllerBase
{
    /// <summary>Includes inactive products and live stock, unlike the storefront listing.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ProductListQuery query, CancellationToken ct)
        => FromResult(await reads.GetAdminProductsAsync(query, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductEditorRequest request, CancellationToken ct)
    {
        var result = await writes.CreateProductAsync(request, CurrentUserId, ct);

        return result.IsSuccess
            ? CreatedFromResult(result, $"/api/admin/products/{result.Value}")
            : Problem(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductEditorRequest request, CancellationToken ct)
        => FromResult(await writes.UpdateProductAsync(id, request, CurrentUserId, ct),
                      StatusCodes.Status204NoContent);

    /// <summary>
    /// Owner-only, and an archive rather than a delete: the row stays, so the
    /// sales history that references it stays whole and the product can be put
    /// back with <see cref="Restore"/>.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => FromResult(await writes.DeleteProductAsync(id, CurrentUserId, ct));

    /// <summary>Puts an archived product back in the shop.</summary>
    /// <remarks>
    /// A POST rather than a DELETE-undo, because it is its own action and can
    /// fail on its own terms — something else may have taken the name, or the
    /// category it belonged to may have been switched off while it was away.
    /// </remarks>
    [HttpPost("{id:int}/restore")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    public async Task<IActionResult> Restore(int id, CancellationToken ct)
        => FromResult(await writes.RestoreProductAsync(id, CurrentUserId, ct));
}
