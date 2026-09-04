using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces;

public interface IOperationsService
{
    Task<OperationResult<InventoryResponse>>     GetInventoryAsync(InventorySearchQuery query, CancellationToken ct = default);
    Task<OperationResult<StockAdjustedResponse>> AdjustStockAsync(int productId, AdjustStockRequest request, string? actorUserId, CancellationToken ct = default);

    Task<OperationResult<StitchingBoardResponse>> GetStitchingBoardAsync(CancellationToken ct = default);
    Task<OperationResult<int>>  CreateStitchingJobAsync(StitchingJobCreateRequest request, CancellationToken ct = default);
    Task<OperationResult<bool>> UpdateStitchingJobAsync(int jobId, StitchingJobUpdateRequest request, CancellationToken ct = default);

    Task<OperationResult<PagedResponse<CustomerResponse>>> SearchCustomersAsync(CustomerSearchQuery query, CancellationToken ct = default);
}
