using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces;

public interface IOrderRepository
{
    /// <summary>
    /// Places an order. Everything that matters — stock check, pricing, totals,
    /// the delivery charge — happens inside the procedure's transaction under
    /// row locks, so nothing here needs to guard against a race.
    /// </summary>
    Task<ProcedureResult<OrderWithLines>> CreateAsync(
        PlaceOrderRequest request, string normalisedPhone, CancellationToken ct = default);

    Task<ProcedureResult<OrderWithLines>> GetByReferenceAsync(
        string reference, string normalisedPhone, CancellationToken ct = default);

    Task<ProcedureResult<(IReadOnlyList<Order> Orders, PageInfo Page)>> SearchAsync(
        OrderSearchQuery query, CancellationToken ct = default);

    Task<ProcedureResult<OrderDetail>> GetByIdAsync(int orderId, CancellationToken ct = default);

    Task<ProcedureResult<bool>> UpdateStatusAsync(
        int orderId, string newStatus, string? note, string? actorUserId, CancellationToken ct = default);

    Task<ProcedureResult<bool>> SaveMeasurementsAsync(
        int orderId, SaveMeasurementsRequest request, string? actorUserId, CancellationToken ct = default);

    Task<ProcedureResult<SetBuilderQuote>> QuoteSetAsync(
        SetBuilderQuoteRequest request, CancellationToken ct = default);
}

public sealed class OrderWithLines
{
    public Order?                    Order { get; init; }
    public IReadOnlyList<OrderLine>  Lines { get; init; } = [];
}

public sealed class OrderDetail
{
    public Order?                            Order        { get; init; }
    public IReadOnlyList<OrderLine>          Lines        { get; init; } = [];
    public IReadOnlyList<OrderMeasurement>   Measurements { get; init; } = [];
    public IReadOnlyList<OrderStatusChange>  History      { get; init; } = [];
}

public sealed class SetBuilderQuote
{
    public IReadOnlyList<SetBuilderQuoteLine> Lines   { get; init; } = [];
    public decimal                            Total   { get; init; }
    public string                             BedSize { get; init; } = string.Empty;
}

public sealed class SetBuilderQuoteLine
{
    public string  Slot        { get; set; } = string.Empty;
    public int     ProductId   { get; set; }
    public string  ProductName { get; set; } = string.Empty;
    public decimal UnitPrice   { get; set; }
    public bool    InStock     { get; set; }
}
