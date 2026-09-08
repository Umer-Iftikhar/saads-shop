using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Repositories.Interfaces.Queries;

namespace SaadsShop.Api.Repositories.Implementations.Queries;

public sealed class OperationsQueryRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), IOperationsQueryRepository
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

    private sealed class StageCount
    {
        public string Stage    { get; set; } = string.Empty;
        public int    JobCount { get; set; }
    }
}
