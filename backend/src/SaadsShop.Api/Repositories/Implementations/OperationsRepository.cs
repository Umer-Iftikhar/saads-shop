using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;

namespace SaadsShop.Api.Repositories.Implementations;

public sealed class OperationsRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), IOperationsRepository
{
    public Task<ProcedureResult<InventorySnapshot>> GetInventoryAsync(
        InventorySearchQuery query, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.InventoryGetList,
            new
            {
                Search       = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search,
                query.LowStockOnly
            },
            async grid =>
            {
                var items   = (await grid.ReadAsync<InventoryItem>()).AsList();
                var totals  = await grid.ReadSingleAsync<InventoryTotals>();

                return new InventorySnapshot
                {
                    Items         = items,
                    ProductCount  = totals.ProductCount,
                    LowStockCount = totals.LowStockCount
                };
            },
            ct);

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

    public Task<ProcedureResult<IReadOnlyList<StitchingJob>>> GetStitchingQueueAsync(CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<StitchingJob>>(
            StoredProcedures.StitchingQueueGet,
            parameters: null,
            async grid =>
            {
                var jobs = (await grid.ReadAsync<StitchingJob>()).AsList();
                // The procedure also returns per-stage counts. They are derivable
                // from the jobs, so they are read and discarded rather than
                // plumbed through — but they MUST be read, or the status row
                // that follows would be mistaken for them.
                _ = await grid.ReadAsync<StageCount>();
                return jobs;
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

    public Task<ProcedureResult<(IReadOnlyList<Customer> Customers, PageInfo Page)>> SearchCustomersAsync(
        CustomerSearchQuery query, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.CustomerGetList,
            new
            {
                Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search,
                query.Page,
                query.PageSize
            },
            async grid =>
            {
                var customers = (await grid.ReadAsync<Customer>()).AsList();
                var page      = await grid.ReadSingleAsync<PageInfo>();
                return ((IReadOnlyList<Customer>)customers, page);
            },
            ct);

    private sealed class InventoryTotals
    {
        public int ProductCount  { get; set; }
        public int LowStockCount { get; set; }
    }

    private sealed class StockRow
    {
        public int ProductId { get; set; }
        public int Stock     { get; set; }
    }

    private sealed class StageCount
    {
        public string Stage    { get; set; } = string.Empty;
        public int    JobCount { get; set; }
    }

    private sealed class CreatedJob
    {
        public int? StitchingJobId { get; set; }
    }
}
