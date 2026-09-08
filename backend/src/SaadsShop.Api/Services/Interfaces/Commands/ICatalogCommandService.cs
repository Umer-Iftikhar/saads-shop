using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;

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
}
