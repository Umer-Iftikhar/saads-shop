using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Repositories.Interfaces.Commands;

namespace SaadsShop.Api.Repositories.Implementations.Commands;

public sealed class OperationsCommandRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), IOperationsCommandRepository
{
    public Task<ProcedureResult<(int ProductId, int Stock)>> AdjustStockAsync(
        int productId, int delta, string reason, string? actorUserId, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.ProductAdjustStock,
            new { ProductId = productId, Delta = delta, Reason = reason, ActorUserId = actorUserId },
            async grid =>
            {
                var row = await grid.ReadSingleOrDefaultAsync<StockRow>();
                return (row?.ProductId ?? productId, row?.Stock ?? 0);
            },
            ct);

    public Task<ProcedureResult<int?>> CreateStitchingJobAsync(
        StitchingJobCreateRequest request, CancellationToken ct = default)
        => ExecuteAsync<int?>(
            StoredProcedures.StitchingJobCreate,
            new
            {
                request.OrderId,
                request.Title,
                request.AssignedTo,
                request.SwatchId,
                request.DueDate,
                request.OrderLineId
            },
            async grid =>
            {
                var created = await grid.ReadSingleOrDefaultAsync<CreatedJob>();
                return created?.StitchingJobId;
            },
            ct);

    public Task<ProcedureResult<bool>> UpdateStitchingJobAsync(
        int jobId, StitchingJobUpdateRequest request, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.StitchingJobUpdate,
            new
            {
                StitchingJobId = jobId,
                request.Stage,
                request.AssignedTo,
                request.DueDate,
                request.ClearDueDate
            },
            ct);

    private sealed class StockRow
    {
        public int ProductId { get; set; }
        public int Stock     { get; set; }
    }

    private sealed class CreatedJob
    {
        public int? StitchingJobId { get; set; }
    }
}
