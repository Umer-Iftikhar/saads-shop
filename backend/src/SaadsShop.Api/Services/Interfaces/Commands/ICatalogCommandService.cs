using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces.Commands;

/// <summary>Changing the catalogue. Owner-gated at the controller, audited below.</summary>
public interface ICatalogCommandService
{
    Task<OperationResult<int>> CreateProductAsync(
        ProductEditorRequest request, string? actorUserId, CancellationToken ct = default);

    Task<OperationResult<bool>> UpdateProductAsync(
        int productId, ProductEditorRequest request, string? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Archives a product rather than deleting it, and can be undone with
    /// <see cref="RestoreProductAsync"/>.
    /// </summary>
    Task<OperationResult<bool>> DeleteProductAsync(
        int productId, string? actorUserId, CancellationToken ct = default);

    Task<OperationResult<bool>> RestoreProductAsync(
        int productId, string? actorUserId, CancellationToken ct = default);

    /// <summary>Validates and stores an uploaded photograph against a product.</summary>
    Task<OperationResult<ProductImageResponse>> SetProductImageAsync(
        int productId, Stream content, string fileName, long length,
        string? actorUserId, CancellationToken ct = default);

    /// <summary>Removes a product's photograph, returning it to the drawn cloth.</summary>
    Task<OperationResult<bool>> RemoveProductImageAsync(
        int productId, string? actorUserId, CancellationToken ct = default);
}
