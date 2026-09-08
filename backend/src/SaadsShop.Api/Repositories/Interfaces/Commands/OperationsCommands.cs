using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;

namespace SaadsShop.Api.Repositories.Interfaces.Commands;

/// <summary>Inventory and the stitching floor — written.</summary>
public interface IOperationsCommandRepository
{
    /// <summary>
    /// Moves stock by a signed amount and returns the new count. Every movement
    /// is written to the audit table with its reason, which is what makes
    /// "where did four sheets go" an answerable question.
    /// </summary>
    Task<ProcedureResult<(int ProductId, int Stock)>> AdjustStockAsync(
        int productId, int delta, string reason, string? actorUserId, CancellationToken ct = default);

    Task<ProcedureResult<int?>> CreateStitchingJobAsync(
        StitchingJobCreateRequest request, CancellationToken ct = default);

    Task<ProcedureResult<bool>> UpdateStitchingJobAsync(
        int jobId, StitchingJobUpdateRequest request, CancellationToken ct = default);
}
