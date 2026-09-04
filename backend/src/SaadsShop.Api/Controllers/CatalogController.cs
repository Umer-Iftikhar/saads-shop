using Microsoft.AspNetCore.Mvc;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Controllers;

/// <summary>The storefront catalogue. Anonymous — browsing needs no account.</summary>
[Route("api/catalog")]
public sealed class CatalogController(ICatalogService catalog) : ApiControllerBase
{
    [HttpGet("products")]
    public async Task<IActionResult> GetProducts([FromQuery] ProductListQuery query, CancellationToken ct)
        => FromResult(await catalog.GetStorefrontProductsAsync(query, ct));

    [HttpGet("products/{id:int}")]
    public async Task<IActionResult> GetProduct(int id, CancellationToken ct)
        => FromResult(await catalog.GetProductAsync(id, null, ct));

    /// <summary>
    /// By slug, so the storefront can use readable URLs
    /// (/wedding-sets/gulaab-bridal-set) without exposing database ids.
    /// </summary>
    [HttpGet("products/by-slug/{slug}")]
    public async Task<IActionResult> GetProductBySlug(string slug, CancellationToken ct)
        => FromResult(await catalog.GetProductAsync(null, slug, ct));

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
        => FromResult(await catalog.GetCategoriesAsync(ct));

    [HttpGet("swatches")]
    public async Task<IActionResult> GetSwatches(CancellationToken ct)
        => FromResult(await catalog.GetSwatchesAsync(ct));

    [HttpGet("bed-sizes")]
    public async Task<IActionResult> GetBedSizes(CancellationToken ct)
        => FromResult(await catalog.GetBedSizesAsync(ct));
}
