using SaadsShop.Api.Common;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Repositories.Interfaces.Queries;
using SaadsShop.Api.Services.Interfaces.Queries;

namespace SaadsShop.Api.Services.Implementations.Queries;

public sealed class OrderQueryService(IOrderQueryRepository repository) : IOrderQueryService
{
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
            Lines          = result.Data.Lines.Select(OrderMapping.ToLineResponse).ToList()
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
            return OperationResult<SetBuilderQuoteResponse>.FromProcedureFailure(result);

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
            return OperationResult<OrderListResponse>.FromProcedureFailure(result);

        var page = result.Data.Page;

        return OperationResult<OrderListResponse>.Success(new OrderListResponse
        {
            Items               = result.Data.Orders.Select(OrderMapping.ToSummaryResponse).ToList(),
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
            Lines           = result.Data.Lines.Select(OrderMapping.ToLineResponse).ToList(),
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
}
