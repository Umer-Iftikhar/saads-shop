using SaadsShop.Api.Common;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces.Commands;
using SaadsShop.Api.Services.Interfaces;
using SaadsShop.Api.Services.Interfaces.Commands;

namespace SaadsShop.Api.Services.Implementations.Commands;

/// <summary>
/// The write side of orders.
/// </summary>
/// <remarks>
/// This is where the cache is invalidated: placing or cancelling an order moves
/// stock, and the storefront's catalogue is cached. Only the command side ever
/// bumps a version, and only after a write has actually succeeded — which is
/// exactly the seam CQRS is for.
/// </remarks>
public sealed class OrderCommandService(
    IOrderCommandRepository repository,
    ICacheService cache,
    ILogger<OrderCommandService> logger) : IOrderCommandService
{
    public async Task<OperationResult<OrderConfirmationResponse>> PlaceOrderAsync(
        PlaceOrderRequest request, CancellationToken ct = default)
    {
        // The regex on the DTO accepts several human spellings; the database
        // stores exactly one. Normalise before the call so one customer is one
        // row, however they typed their number.
        var phone = PhoneNumber.Normalise(request.Phone);

        if (phone is null)
        {
            return OperationResult<OrderConfirmationResponse>.Invalid(
                new Dictionary<string, string[]>
                {
                    [nameof(PlaceOrderRequest.Phone)] =
                        ["That phone number does not look right. Use the form 03xx xxx xxxx."]
                });
        }

        var result = await repository.CreateAsync(request, phone, ct);

        if (!result.IsSuccess || result.Data?.Order is null)
        {
            // Not an error worth a stack trace — an item selling out mid-checkout
            // is ordinary. Logged at Information so the shop can still see how
            // often it happens during shaadi season.
            logger.LogInformation(
                "Checkout refused ({ResponseCode}): {ResponseMessage}",
                result.ResponseCode, result.ResponseMessage);

            return OperationResult<OrderConfirmationResponse>.FromProcedureFailure(result);
        }

        var order = result.Data.Order;

        // Stock moved, so the storefront's cached catalogue is stale.
        cache.BumpVersion(CacheKeys.CatalogVersion);

        logger.LogInformation(
            "Order {Reference} placed, {LineCount} line(s), total {Total} PKR, paying {PaymentMethod}",
            order.Reference, result.Data.Lines.Count, order.Total, order.PaymentMethod);

        return OperationResult<OrderConfirmationResponse>.Success(
            new OrderConfirmationResponse
            {
                Reference       = order.Reference,
                Status          = order.Status,
                PaymentMethod   = order.PaymentMethod,
                Subtotal        = order.Subtotal,
                DeliveryCharge  = order.DeliveryCharge,
                Total           = order.Total,
                DeliveryAddress = order.DeliveryAddress,
                CustomerName    = order.CustomerName,
                PlacedAt        = order.PlacedAt,
                Lines           = result.Data.Lines.Select(OrderMapping.ToLineResponse).ToList()
            },
            result.ResponseMessage);
    }

    public async Task<OperationResult<bool>> UpdateStatusAsync(
        int orderId, UpdateOrderStatusRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var result = await repository.UpdateStatusAsync(orderId, request.Status, request.Note, actorUserId, ct);

        if (!result.IsSuccess)
            return OperationResult<bool>.Failure(result.ResponseCode, result.ResponseMessage);

        // Cancelling returns stock to the shelf, so the catalogue is stale.
        if (string.Equals(request.Status, nameof(OrderStatus.Cancelled), StringComparison.Ordinal))
            cache.BumpVersion(CacheKeys.CatalogVersion);

        logger.LogInformation(
            "Order {OrderId} moved to {Status} by {ActorUserId}", orderId, request.Status, actorUserId);

        return OperationResult<bool>.Success(true, result.ResponseMessage);
    }

    public async Task<OperationResult<bool>> SaveMeasurementsAsync(
        int orderId, SaveMeasurementsRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var result = await repository.SaveMeasurementsAsync(orderId, request, actorUserId, ct);

        return result.IsSuccess
            ? OperationResult<bool>.Success(true, result.ResponseMessage)
            : OperationResult<bool>.Failure(result.ResponseCode, result.ResponseMessage);
    }
}
