using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces;

public interface ICatalogService
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

    Task<OperationResult<int>>  CreateProductAsync(ProductEditorRequest request, string? actorUserId, CancellationToken ct = default);
    Task<OperationResult<bool>> UpdateProductAsync(int productId, ProductEditorRequest request, string? actorUserId, CancellationToken ct = default);
    Task<OperationResult<bool>> DeleteProductAsync(int productId, string? actorUserId, CancellationToken ct = default);
}
