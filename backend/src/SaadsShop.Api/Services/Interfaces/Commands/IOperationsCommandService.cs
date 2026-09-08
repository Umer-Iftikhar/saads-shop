using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces.Commands;

/// <summary>Moving stock and the stitching floor along.</summary>
public interface IOperationsCommandService
{
    Task<OperationResult<StockAdjustedResponse>> AdjustStockAsync(
        int productId, AdjustStockRequest request, string? actorUserId, CancellationToken ct = default);

    Task<OperationResult<int>> CreateStitchingJobAsync(
        StitchingJobCreateRequest request, CancellationToken ct = default);

    Task<OperationResult<bool>> UpdateStitchingJobAsync(
        int jobId, StitchingJobUpdateRequest request, CancellationToken ct = default);
}
