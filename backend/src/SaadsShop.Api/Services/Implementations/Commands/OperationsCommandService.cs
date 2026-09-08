using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Repositories.Interfaces.Commands;
using SaadsShop.Api.Services.Interfaces;
using SaadsShop.Api.Services.Interfaces.Commands;

namespace SaadsShop.Api.Services.Implementations.Commands;

public sealed class OperationsCommandService(
    IOperationsCommandRepository repository,
    ICacheService cache,
    ILogger<OperationsCommandService> logger) : IOperationsCommandService
{
    public async Task<OperationResult<StockAdjustedResponse>> AdjustStockAsync(
        int productId, AdjustStockRequest request, string? actorUserId, CancellationToken ct = default)
    {
        // Caught here as well as in the procedure: [Range] permits zero because
        // it has to span negative and positive, so zero needs its own rule.
        if (request.Delta == 0)
        {
            return OperationResult<StockAdjustedResponse>.Invalid(new Dictionary<string, string[]>
            {
                [nameof(AdjustStockRequest.Delta)] = ["Enter how many pieces to add or remove."]
            });
        }

        var result = await repository.AdjustStockAsync(productId, request.Delta, request.Reason, actorUserId, ct);

        if (!result.IsSuccess)
            return OperationResult<StockAdjustedResponse>.Failure(result.ResponseCode, result.ResponseMessage);

        cache.BumpVersion(CacheKeys.CatalogVersion);

        logger.LogInformation(
            "Stock for product {ProductId} adjusted by {Delta} to {Stock} by {ActorUserId}: {Reason}",
            productId, request.Delta, result.Data.Stock, actorUserId, request.Reason);

        return OperationResult<StockAdjustedResponse>.Success(
            new StockAdjustedResponse { ProductId = result.Data.ProductId, Stock = result.Data.Stock },
            result.ResponseMessage);
    }

    public async Task<OperationResult<int>> CreateStitchingJobAsync(
        StitchingJobCreateRequest request, CancellationToken ct = default)
    {
        var result = await repository.CreateStitchingJobAsync(request, ct);

        return result.IsSuccess && result.Data is not null
            ? OperationResult<int>.Success(result.Data.Value, result.ResponseMessage)
            : OperationResult<int>.Failure(result.ResponseCode, result.ResponseMessage);
    }

    public async Task<OperationResult<bool>> UpdateStitchingJobAsync(
        int jobId, StitchingJobUpdateRequest request, CancellationToken ct = default)
    {
        var result = await repository.UpdateStitchingJobAsync(jobId, request, ct);

        return result.IsSuccess
            ? OperationResult<bool>.Success(true, result.ResponseMessage)
            : OperationResult<bool>.Failure(result.ResponseCode, result.ResponseMessage);
    }
}
