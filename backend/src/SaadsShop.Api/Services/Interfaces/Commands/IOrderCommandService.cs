using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;

namespace SaadsShop.Api.Services.Interfaces.Commands;

/// <summary>Everything that changes an order.</summary>
public interface IOrderCommandService
{
    Task<OperationResult<OrderConfirmationResponse>> PlaceOrderAsync(
        PlaceOrderRequest request, CancellationToken ct = default);

    Task<OperationResult<bool>> UpdateStatusAsync(
        int orderId, UpdateOrderStatusRequest request, string? actorUserId, CancellationToken ct = default);

    Task<OperationResult<bool>> SaveMeasurementsAsync(
        int orderId, SaveMeasurementsRequest request, string? actorUserId, CancellationToken ct = default);
}
