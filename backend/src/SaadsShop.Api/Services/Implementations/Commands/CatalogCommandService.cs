using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Repositories.Interfaces.Commands;
using SaadsShop.Api.Services.Interfaces;
using SaadsShop.Api.Services.Interfaces.Commands;

namespace SaadsShop.Api.Services.Implementations.Commands;

public sealed class CatalogCommandService(
    ICatalogCommandRepository repository,
    IProductImageService images,
    ICacheService cache,
    ILogger<CatalogCommandService> logger) : ICatalogCommandService
{
    public async Task<OperationResult<int>> CreateProductAsync(
        ProductEditorRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var result = await repository.CreateProductAsync(request, actorUserId, ct);

        if (!result.IsSuccess || result.Data is null)
            return OperationResult<int>.FromProcedureFailure(result);

        InvalidateCatalog();
        logger.LogInformation("Product {ProductId} created by {ActorUserId}", result.Data, actorUserId);

        return OperationResult<int>.Success(result.Data.Value, result.ResponseMessage);
    }

    public async Task<OperationResult<bool>> UpdateProductAsync(
        int productId, ProductEditorRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var result = await repository.UpdateProductAsync(productId, request, actorUserId, ct);

        if (!result.IsSuccess)
            return OperationResult<bool>.Failure(result.ResponseCode, result.ResponseMessage);

        InvalidateCatalog();
        logger.LogInformation("Product {ProductId} updated by {ActorUserId}", productId, actorUserId);

        return OperationResult<bool>.Success(true, result.ResponseMessage);
    }

    public async Task<OperationResult<bool>> DeleteProductAsync(
        int productId, string? actorUserId, CancellationToken ct = default)
    {
        var result = await repository.DeleteProductAsync(productId, actorUserId, ct);

        if (!result.IsSuccess)
            return OperationResult<bool>.Failure(result.ResponseCode, result.ResponseMessage);

        InvalidateCatalog();
        logger.LogInformation("Product {ProductId} archived by {ActorUserId}", productId, actorUserId);

        return OperationResult<bool>.Success(true, result.ResponseMessage);
    }

    public async Task<OperationResult<bool>> RestoreProductAsync(
        int productId, string? actorUserId, CancellationToken ct = default)
    {
        var result = await repository.RestoreProductAsync(productId, actorUserId, ct);

        if (!result.IsSuccess)
            return OperationResult<bool>.Failure(result.ResponseCode, result.ResponseMessage);

        // The storefront cached the catalogue without this product in it.
        InvalidateCatalog();
        logger.LogInformation("Product {ProductId} restored by {ActorUserId}", productId, actorUserId);

        return OperationResult<bool>.Success(true, result.ResponseMessage);
    }

    /// <summary>
    /// Stores an uploaded photograph against a product.
    /// </summary>
    /// <remarks>
    /// The file is written first and the row second, so a failure between the
    /// two leaves an orphaned file rather than a row pointing at nothing. That
    /// is the right way round: a stray file costs disk, whereas a product whose
    /// photo 404s is broken on every screen it appears on. The orphan is cleaned
    /// up here when the row write fails.
    /// </remarks>
    public async Task<OperationResult<ProductImageResponse>> SetProductImageAsync(
        int productId, Stream content, string fileName, long length,
        string? actorUserId, CancellationToken ct = default)
    {
        var stored = await images.SaveAsync(content, fileName, length, ct);

        if (!stored.IsSuccess || stored.Value is null)
            return OperationResult<ProductImageResponse>.FromFailure(stored);

        var written = await repository.SetImageAsync(
            productId, stored.Value.ImagePath, stored.Value.ThumbnailPath, actorUserId, ct);

        if (!written.IsSuccess)
        {
            // The row never took the new photo, so the files it would have
            // pointed at are rubbish.
            images.TryDelete(stored.Value.ImagePath, stored.Value.ThumbnailPath);
            return OperationResult<ProductImageResponse>.Failure(written.ResponseCode, written.ResponseMessage);
        }

        // Whatever the product had before is now unreferenced.
        images.TryDelete(written.Data?.ImagePath, written.Data?.ThumbnailPath);

        InvalidateCatalog();
        logger.LogInformation("Photo set on product {ProductId} by {ActorUserId}", productId, actorUserId);

        return OperationResult<ProductImageResponse>.Success(new ProductImageResponse
        {
            ImagePath     = stored.Value.ImagePath,
            ThumbnailPath = stored.Value.ThumbnailPath,
            Width         = stored.Value.Width,
            Height        = stored.Value.Height,
        }, written.ResponseMessage);
    }

    /// <summary>Removes a product's photograph, returning it to the drawn cloth.</summary>
    public async Task<OperationResult<bool>> RemoveProductImageAsync(
        int productId, string? actorUserId, CancellationToken ct = default)
    {
        var written = await repository.SetImageAsync(productId, null, null, actorUserId, ct);

        if (!written.IsSuccess)
            return OperationResult<bool>.Failure(written.ResponseCode, written.ResponseMessage);

        images.TryDelete(written.Data?.ImagePath, written.Data?.ThumbnailPath);

        InvalidateCatalog();
        logger.LogInformation("Photo removed from product {ProductId} by {ActorUserId}", productId, actorUserId);

        return OperationResult<bool>.Success(true, written.ResponseMessage);
    }

    /// <summary>
    /// Bumped only after a write has actually succeeded. Bumping first would
    /// open a window where a concurrent read repopulates the new version with
    /// pre-write data — the exact staleness the cache is supposed to prevent.
    /// </summary>
    private void InvalidateCatalog() => cache.BumpVersion(CacheKeys.CatalogVersion);
}
