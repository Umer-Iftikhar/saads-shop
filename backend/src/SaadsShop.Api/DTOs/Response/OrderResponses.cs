namespace SaadsShop.Api.DTOs.Response;

public sealed class OrderLineResponse
{
    public int     OrderLineId      { get; init; }
    public int     ProductId        { get; init; }
    public string  ProductName      { get; init; } = string.Empty;
    public string? SwatchName       { get; init; }
    public string? SwatchColorValue { get; init; }
    public string? SwatchWeave      { get; init; }
    public string? BedSize          { get; init; }
    public decimal UnitPrice        { get; init; }
    public int     Quantity         { get; init; }
    public decimal LineTotal        { get; init; }
}

public sealed class OrderConfirmationResponse
{
    public string   Reference       { get; init; } = string.Empty;
    public string   Status          { get; init; } = string.Empty;
    public string   PaymentMethod   { get; init; } = string.Empty;
    public decimal  Subtotal        { get; init; }
    public decimal  DeliveryCharge  { get; init; }
    public decimal  Total           { get; init; }
    public string   DeliveryAddress { get; init; } = string.Empty;
    public string   CustomerName    { get; init; } = string.Empty;
    public DateTime PlacedAt        { get; init; }
    public IReadOnlyList<OrderLineResponse> Lines { get; init; } = [];
}

public sealed class OrderSummaryResponse
{
    public int      OrderId       { get; init; }
    public string   Reference     { get; init; } = string.Empty;
    public DateTime PlacedAt      { get; init; }
    public string   CustomerName  { get; init; } = string.Empty;
    public string?  Phone         { get; init; }
    public string?  ItemSummary   { get; init; }
    public int      LineCount     { get; init; }
    public decimal  Total         { get; init; }
    public string   PaymentMethod { get; init; } = string.Empty;
    public string   Status        { get; init; } = string.Empty;
}

public sealed class OrderListResponse
{
    public IReadOnlyList<OrderSummaryResponse> Items { get; init; } = [];
    public int TotalCount          { get; init; }
    public int NeedsAttentionCount { get; init; }
    public int Page                { get; init; }
    public int PageSize            { get; init; }
    public int TotalPages          { get; init; }
}

public sealed class MeasurementResponse
{
    public decimal? BedWidthIn   { get; init; }
    public decimal? BedLengthIn  { get; init; }
    public decimal? WindowDropIn { get; init; }
    public int?     WindowCount  { get; init; }
    public string?  Notes        { get; init; }
    public string?  TakenBy      { get; init; }
    public DateTime TakenAt      { get; init; }
}

public sealed class StatusChangeResponse
{
    public string?  FromStatus { get; init; }
    public string   ToStatus   { get; init; } = string.Empty;
    public string?  Note       { get; init; }
    public string?  ChangedBy  { get; init; }
    public DateTime ChangedAt  { get; init; }
}

public sealed class OrderDetailResponse
{
    public int      OrderId         { get; init; }
    public string   Reference       { get; init; } = string.Empty;
    public string   Status          { get; init; } = string.Empty;
    public string   PaymentMethod   { get; init; } = string.Empty;
    public decimal  Subtotal        { get; init; }
    public decimal  DeliveryCharge  { get; init; }
    public decimal  Total           { get; init; }
    public string   DeliveryAddress { get; init; } = string.Empty;
    public string?  Notes           { get; init; }
    public DateTime PlacedAt        { get; init; }

    public int      CustomerId      { get; init; }
    public string   CustomerName    { get; init; } = string.Empty;
    public string?  Phone           { get; init; }
    public string?  Area            { get; init; }

    public IReadOnlyList<OrderLineResponse>    Lines        { get; init; } = [];
    public IReadOnlyList<MeasurementResponse>  Measurements { get; init; } = [];
    public IReadOnlyList<StatusChangeResponse> History      { get; init; } = [];
}

/// <summary>Customer-facing tracking view — no internal ids, no cost breakdowns beyond the total.</summary>
public sealed class OrderTrackingResponse
{
    public string   Reference      { get; init; } = string.Empty;
    public string   Status         { get; init; } = string.Empty;
    public string   PaymentMethod  { get; init; } = string.Empty;
    public decimal  Subtotal       { get; init; }
    public decimal  DeliveryCharge { get; init; }
    public decimal  Total          { get; init; }
    public string   CustomerName   { get; init; } = string.Empty;
    public DateTime PlacedAt       { get; init; }
    public IReadOnlyList<OrderLineResponse> Lines { get; init; } = [];
}

public sealed class SetBuilderLineResponse
{
    /// <summary>Bistar, Parde or Cushions.</summary>
    public string  Slot        { get; init; } = string.Empty;
    public int     ProductId   { get; init; }
    public string  ProductName { get; init; } = string.Empty;
    public decimal UnitPrice   { get; init; }
    public bool    InStock     { get; init; }
}

public sealed class SetBuilderQuoteResponse
{
    public string  BedSize { get; init; } = string.Empty;
    public decimal Total   { get; init; }
    public IReadOnlyList<SetBuilderLineResponse> Lines { get; init; } = [];
}
