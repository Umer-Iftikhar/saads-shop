using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces;

/// <summary>Inventory, the stitching floor and customers.</summary>
public interface IOperationsRepository
{
    Task<ProcedureResult<InventorySnapshot>> GetInventoryAsync(
        InventorySearchQuery query, CancellationToken ct = default);

    Task<ProcedureResult<(int ProductId, int Stock)>> AdjustStockAsync(
        int productId, int delta, string reason, string? actorUserId, CancellationToken ct = default);

    Task<ProcedureResult<IReadOnlyList<StitchingJob>>> GetStitchingQueueAsync(CancellationToken ct = default);

    Task<ProcedureResult<int?>> CreateStitchingJobAsync(
        StitchingJobCreateRequest request, CancellationToken ct = default);

    Task<ProcedureResult<bool>> UpdateStitchingJobAsync(
        int jobId, StitchingJobUpdateRequest request, CancellationToken ct = default);

    Task<ProcedureResult<(IReadOnlyList<Customer> Customers, PageInfo Page)>> SearchCustomersAsync(
        CustomerSearchQuery query, CancellationToken ct = default);
}

public sealed class InventorySnapshot
{
    public IReadOnlyList<InventoryItem> Items         { get; init; } = [];
    public int                          ProductCount  { get; init; }
    public int                          LowStockCount { get; init; }
}
