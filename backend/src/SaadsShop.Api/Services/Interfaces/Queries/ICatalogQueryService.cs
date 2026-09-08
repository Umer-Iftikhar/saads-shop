using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces.Queries;

/// <summary>
/// The catalogue as it is read.
/// </summary>
/// <remarks>
/// The storefront and the panel get different projections of the same products
/// — one says whether an item is in stock, the other says how many are left —
/// and they are separate methods rather than a flag, so no caller can ask for
/// the admin shape by accident.
/// </remarks>
public interface ICatalogQueryService
{
    Task<OperationResult<IReadOnlyList<CategoryResponse>>> GetCategoriesAsync(CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<SwatchResponse>>>   GetSwatchesAsync(CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<BedSizeResponse>>>  GetBedSizesAsync(CancellationToken ct = default);

    Task<OperationResult<PagedResponse<ProductSummaryResponse>>> GetStorefrontProductsAsync(
        ProductListQuery query, CancellationToken ct = default);

    Task<OperationResult<ProductDetailResponse>> GetProductAsync(
        int? productId, string? slug, CancellationToken ct = default);

    Task<OperationResult<PagedResponse<ProductAdminResponse>>> GetAdminProductsAsync(
        ProductListQuery query, CancellationToken ct = default);
}
