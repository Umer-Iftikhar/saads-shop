using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Controllers;

/// <summary>The product editor.</summary>
[Route("api/admin/products")]
[Authorize(Policy = AuthPolicies.StaffOnly)]
public sealed class AdminCatalogController(ICatalogService catalog) : ApiControllerBase
{
    /// <summary>Includes inactive products and live stock, unlike the storefront listing.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ProductListQuery query, CancellationToken ct)
        => FromResult(await catalog.GetAdminProductsAsync(query, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductEditorRequest request, CancellationToken ct)
    {
        var result = await catalog.CreateProductAsync(request, CurrentUserId, ct);

        return result.IsSuccess
            ? CreatedFromResult(result, $"/api/admin/products/{result.Value}")
            : Problem(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductEditorRequest request, CancellationToken ct)
        => FromResult(await catalog.UpdateProductAsync(id, request, CurrentUserId, ct),
                      StatusCodes.Status204NoContent);

    /// <summary>
    /// Owner-only, and a soft delete: a product on past orders is hidden from
    /// the shop rather than removed, so sales history stays intact.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => FromResult(await catalog.DeleteProductAsync(id, CurrentUserId, ct));
}
