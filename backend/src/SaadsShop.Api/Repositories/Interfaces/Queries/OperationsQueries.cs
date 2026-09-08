using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces.Queries;

/// <summary>Inventory, the stitching floor and customers — read.</summary>
public interface IOperationsQueryRepository
{
    Task<ProcedureResult<InventorySnapshot>> GetInventoryAsync(
        InventorySearchQuery query, CancellationToken ct = default);

    Task<ProcedureResult<IReadOnlyList<StitchingJob>>> GetStitchingQueueAsync(CancellationToken ct = default);

    Task<ProcedureResult<(IReadOnlyList<Customer> Customers, PageInfo Page)>> SearchCustomersAsync(
        CustomerSearchQuery query, CancellationToken ct = default);
}
