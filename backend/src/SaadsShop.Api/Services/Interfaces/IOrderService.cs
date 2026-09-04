using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces;

public interface IOrderService
{
    Task<OperationResult<OrderConfirmationResponse>> PlaceOrderAsync(
        PlaceOrderRequest request, CancellationToken ct = default);

    Task<OperationResult<OrderTrackingResponse>> TrackAsync(
        TrackOrderQuery query, CancellationToken ct = default);

    Task<OperationResult<SetBuilderQuoteResponse>> QuoteSetAsync(
        SetBuilderQuoteRequest request, CancellationToken ct = default);

    Task<OperationResult<OrderListResponse>> SearchAsync(
        OrderSearchQuery query, CancellationToken ct = default);

    Task<OperationResult<OrderDetailResponse>> GetAsync(int orderId, CancellationToken ct = default);

    Task<OperationResult<bool>> UpdateStatusAsync(
        int orderId, UpdateOrderStatusRequest request, string? actorUserId, CancellationToken ct = default);

    Task<OperationResult<bool>> SaveMeasurementsAsync(
        int orderId, SaveMeasurementsRequest request, string? actorUserId, CancellationToken ct = default);
}
