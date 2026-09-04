using SaadsShop.Api.Common;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Services.Implementations;

public sealed class OrderService(
    IOrderRepository repository,
    ICacheService cache,
    ILogger<OrderService> logger) : IOrderService
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

            return OperationResult<OrderConfirmationResponse>
                .Failure(result.ResponseCode, result.ResponseMessage);
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
                Lines           = result.Data.Lines.Select(ToLineResponse).ToList()
            },
            result.ResponseMessage);
    }

    public async Task<OperationResult<OrderTrackingResponse>> TrackAsync(
        TrackOrderQuery query, CancellationToken ct = default)
    {
        var phone = PhoneNumber.Normalise(query.Phone);

        // Same answer as a genuinely unknown order. Telling the caller the phone
        // was malformed versus not matching would turn this into an oracle for
        // discovering which order numbers exist.
        if (phone is null)
            return NotFoundOrder();

        var result = await repository.GetByReferenceAsync(query.Reference, phone, ct);

        if (!result.IsSuccess || result.Data?.Order is null)
            return NotFoundOrder();

        var order = result.Data.Order;

        return OperationResult<OrderTrackingResponse>.Success(new OrderTrackingResponse
        {
            Reference      = order.Reference,
            Status         = order.Status,
            PaymentMethod  = order.PaymentMethod,
            Subtotal       = order.Subtotal,
            DeliveryCharge = order.DeliveryCharge,
            Total          = order.Total,
            CustomerName   = order.CustomerName,
            PlacedAt       = order.PlacedAt,
            Lines          = result.Data.Lines.Select(ToLineResponse).ToList()
        });

        static OperationResult<OrderTrackingResponse> NotFoundOrder()
            => OperationResult<OrderTrackingResponse>.Failure(
                ResponseCodes.NotFound, "We could not find that order.");
    }

    public async Task<OperationResult<SetBuilderQuoteResponse>> QuoteSetAsync(
        SetBuilderQuoteRequest request, CancellationToken ct = default)
    {
        var result = await repository.QuoteSetAsync(request, ct);

        if (!result.IsSuccess || result.Data is null)
            return OperationResult<SetBuilderQuoteResponse>
                .Failure(result.ResponseCode, result.ResponseMessage);

        return OperationResult<SetBuilderQuoteResponse>.Success(new SetBuilderQuoteResponse
        {
            BedSize = result.Data.BedSize,
            Total   = result.Data.Total,
            Lines   = result.Data.Lines.Select(l => new SetBuilderLineResponse
            {
                Slot        = l.Slot,
                ProductId   = l.ProductId,
                ProductName = l.ProductName,
                UnitPrice   = l.UnitPrice,
                InStock     = l.InStock
            }).ToList()
        });
    }

    public async Task<OperationResult<OrderListResponse>> SearchAsync(
        OrderSearchQuery query, CancellationToken ct = default)
    {
        var result = await repository.SearchAsync(query, ct);

        if (!result.IsSuccess || result.Data.Orders is null)
            return OperationResult<OrderListResponse>.Failure(result.ResponseCode, result.ResponseMessage);

        var page = result.Data.Page;

        return OperationResult<OrderListResponse>.Success(new OrderListResponse
        {
            Items               = result.Data.Orders.Select(ToSummaryResponse).ToList(),
            TotalCount          = page.TotalCount,
            NeedsAttentionCount = page.NeedsAttentionCount,
            Page                = page.Page,
            PageSize            = page.PageSize,
            TotalPages          = page.PageSize <= 0 ? 0 : (int)Math.Ceiling(page.TotalCount / (double)page.PageSize)
        });
    }

    public async Task<OperationResult<OrderDetailResponse>> GetAsync(int orderId, CancellationToken ct = default)
    {
        var result = await repository.GetByIdAsync(orderId, ct);

        if (!result.IsSuccess || result.Data?.Order is null)
            return OperationResult<OrderDetailResponse>.Failure(
                result.IsSuccess ? ResponseCodes.NotFound : result.ResponseCode,
                result.IsSuccess ? "That order no longer exists." : result.ResponseMessage);

        var o = result.Data.Order;

        return OperationResult<OrderDetailResponse>.Success(new OrderDetailResponse
        {
            OrderId         = o.OrderId,
            Reference       = o.Reference,
            Status          = o.Status,
            PaymentMethod   = o.PaymentMethod,
            Subtotal        = o.Subtotal,
            DeliveryCharge  = o.DeliveryCharge,
            Total           = o.Total,
            DeliveryAddress = o.DeliveryAddress,
            Notes           = o.Notes,
            PlacedAt        = o.PlacedAt,
            CustomerId      = o.CustomerId,
            CustomerName    = o.CustomerName,
            Phone           = o.Phone,
            Area            = o.Area,
            Lines           = result.Data.Lines.Select(ToLineResponse).ToList(),
            Measurements    = result.Data.Measurements.Select(m => new MeasurementResponse
            {
                BedWidthIn   = m.BedWidthIn,
                BedLengthIn  = m.BedLengthIn,
                WindowDropIn = m.WindowDropIn,
                WindowCount  = m.WindowCount,
                Notes        = m.Notes,
                TakenBy      = m.TakenBy,
                TakenAt      = m.TakenAt
            }).ToList(),
            History = result.Data.History.Select(h => new StatusChangeResponse
            {
                FromStatus = h.FromStatus,
                ToStatus   = h.ToStatus,
                Note       = h.Note,
                ChangedBy  = h.ChangedBy,
                ChangedAt  = h.ChangedAt
            }).ToList()
        });
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

    private static OrderLineResponse ToLineResponse(OrderLine l) => new()
    {
        OrderLineId      = l.OrderLineId,
        ProductId        = l.ProductId,
        ProductName      = l.ProductName,
        SwatchName       = l.SwatchName,
        SwatchColorValue = l.SwatchColorValue,
        SwatchWeave      = l.SwatchWeave,
        BedSize          = l.BedSize,
        UnitPrice        = l.UnitPrice,
        Quantity         = l.Quantity,
        LineTotal        = l.LineTotal
    };

    private static OrderSummaryResponse ToSummaryResponse(Order o) => new()
    {
        OrderId       = o.OrderId,
        Reference     = o.Reference,
        PlacedAt      = o.PlacedAt,
        CustomerName  = o.CustomerName,
        Phone         = o.Phone,
        ItemSummary   = o.ItemSummary,
        LineCount     = o.LineCount,
        Total         = o.Total,
        PaymentMethod = o.PaymentMethod,
        Status        = o.Status
    };
}
