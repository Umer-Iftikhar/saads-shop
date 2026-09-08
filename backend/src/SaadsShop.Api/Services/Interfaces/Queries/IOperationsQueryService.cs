using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces.Queries;

/// <summary>Inventory, the stitching board and customers — read.</summary>
public interface IOperationsQueryService
{
    Task<OperationResult<InventoryResponse>> GetInventoryAsync(
        InventorySearchQuery query, CancellationToken ct = default);

    Task<OperationResult<StitchingBoardResponse>> GetStitchingBoardAsync(CancellationToken ct = default);

    Task<OperationResult<PagedResponse<CustomerResponse>>> SearchCustomersAsync(
        CustomerSearchQuery query, CancellationToken ct = default);
}
