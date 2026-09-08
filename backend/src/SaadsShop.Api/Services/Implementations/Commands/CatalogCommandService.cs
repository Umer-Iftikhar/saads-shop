using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Repositories.Interfaces.Commands;
using SaadsShop.Api.Services.Interfaces;
using SaadsShop.Api.Services.Interfaces.Commands;

namespace SaadsShop.Api.Services.Implementations.Commands;

public sealed class CatalogCommandService(
    ICatalogCommandRepository repository,
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
        logger.LogInformation("Product {ProductId} removed by {ActorUserId}", productId, actorUserId);

        return OperationResult<bool>.Success(true, result.ResponseMessage);
    }

    /// <summary>
    /// Bumped only after a write has actually succeeded. Bumping first would
    /// open a window where a concurrent read repopulates the new version with
    /// pre-write data — the exact staleness the cache is supposed to prevent.
    /// </summary>
    private void InvalidateCatalog() => cache.BumpVersion(CacheKeys.CatalogVersion);
}
