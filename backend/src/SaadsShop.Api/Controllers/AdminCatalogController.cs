using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
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

    /// <summary>
    /// Uploads a photograph for a product.
    /// </summary>
    /// <remarks>
    /// The size cap is enforced three times over, and each is doing a different
    /// job: <see cref="RequestSizeLimitAttribute"/> makes Kestrel reject an
    /// oversized body before it is buffered, the form-options limit stops the
    /// multipart parser from accepting one section that large, and the service
    /// checks the bytes it actually received. Only the first two prevent the
    /// upload being read at all, which is the point of having them.
    /// </remarks>
    [HttpPost("{id:int}/image")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 12 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(int id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(OperationResult<ProductImageResponse>.Failure(
                ResponseCodes.ValidationFailed, "Choose a photo to upload."));
        }

        await using var content = file.OpenReadStream();

        return FromResult(await writes.SetProductImageAsync(
            id, content, file.FileName, file.Length, CurrentUserId, ct));
    }

    /// <summary>Removes a product's photograph; the storefront draws the cloth again.</summary>
    [HttpDelete("{id:int}/image")]
    public async Task<IActionResult> RemoveImage(int id, CancellationToken ct)
        => FromResult(await writes.RemoveProductImageAsync(id, CurrentUserId, ct),
                      StatusCodes.Status204NoContent);

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
