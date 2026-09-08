using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces.Queries;

/// <summary>Everything the shop can be asked about orders.</summary>
public interface IOrderQueryService
{
    /// <summary>A customer's own order, found by reference and phone together.</summary>
    Task<OperationResult<OrderTrackingResponse>> TrackAsync(
        TrackOrderQuery query, CancellationToken ct = default);

    Task<OperationResult<SetBuilderQuoteResponse>> QuoteSetAsync(
        SetBuilderQuoteRequest request, CancellationToken ct = default);

    Task<OperationResult<OrderListResponse>> SearchAsync(
        OrderSearchQuery query, CancellationToken ct = default);

    Task<OperationResult<OrderDetailResponse>> GetAsync(int orderId, CancellationToken ct = default);
}
