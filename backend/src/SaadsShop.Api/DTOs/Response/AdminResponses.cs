namespace SaadsShop.Api.DTOs.Response;

public sealed class InventoryRowResponse
{
    public int     ProductId        { get; init; }
    public string  Name             { get; init; } = string.Empty;
    public string  CategoryName     { get; init; } = string.Empty;
    public decimal Price            { get; init; }
    public int     Stock            { get; init; }
    public int     LowStockAt       { get; init; }

    /// <summary>"Out of stock", "Low — reorder", "Fine", "Plenty" — decided by the database.</summary>
    public string  StockLabel       { get; init; } = string.Empty;

    public int?    DefaultSwatchId  { get; init; }
    public string? SwatchColorValue { get; init; }
    public string? SwatchWeave      { get; init; }
}

public sealed class InventoryResponse
{
    public IReadOnlyList<InventoryRowResponse> Items { get; init; } = [];
    public int ProductCount  { get; init; }
    public int LowStockCount { get; init; }
}

public sealed class StockAdjustedResponse
{
    public int ProductId { get; init; }
    public int Stock     { get; init; }
}

public sealed class StitchingJobResponse
{
    public int       StitchingJobId   { get; init; }
    public int       OrderId          { get; init; }
    public string    Reference        { get; init; } = string.Empty;
    public string    Title            { get; init; } = string.Empty;
    public string    Stage            { get; init; } = string.Empty;
    public string?   AssignedTo       { get; init; }
    public string?   SwatchColorValue { get; init; }
    public string?   SwatchWeave      { get; init; }
    public DateOnly? DueDate          { get; init; }
    public bool      IsOverdue        { get; init; }
}

/// <summary>
/// The board, already grouped into its four columns. Grouping here rather than
/// in the browser keeps the column order — Measuring, Cutting, Stitching, Ready
/// — owned by one place, and an empty column still appears.
/// </summary>
public sealed class StitchingBoardResponse
{
    public IReadOnlyList<StitchingColumnResponse> Columns { get; init; } = [];
}

public sealed class StitchingColumnResponse
{
    public string Stage { get; init; } = string.Empty;
    public int    Count { get; init; }
    public IReadOnlyList<StitchingJobResponse> Jobs { get; init; } = [];
}

public sealed class CustomerResponse
{
    public int       CustomerId  { get; init; }
    public string    Name        { get; init; } = string.Empty;
    public string    Phone       { get; init; } = string.Empty;
    public string?   Area        { get; init; }
    public int       OrderCount  { get; init; }
    public decimal   TotalSpent  { get; init; }
    public DateTime? LastOrderAt { get; init; }
}

public sealed class DashboardStatsResponse
{
    public decimal SalesToday                 { get; init; }
    public decimal SalesSameDayLastWeek       { get; init; }

    /// <summary>
    /// Percentage change against the same weekday last week. Null when last
    /// week was zero — "+∞%" is not a number a shopkeeper can act on.
    /// </summary>
    public decimal? SalesChangePercent        { get; init; }

    public int     OrdersOpen                 { get; init; }
    public int     OrdersAwaitingMeasurements { get; init; }
    public int     JobsOnFloor                { get; init; }
    public int     JobsDueTomorrow            { get; init; }
    public decimal MonthToDateSales           { get; init; }
}

public sealed class SalesWeekResponse
{
    public DateOnly WeekStart  { get; init; }
    public decimal  Sales      { get; init; }
    public int      OrderCount { get; init; }
}

public sealed class BestSellerResponse
{
    public int     ProductId        { get; init; }
    public string  Name             { get; init; } = string.Empty;
    public int     SoldCount        { get; init; }
    public decimal Revenue          { get; init; }
    public string? SwatchColorValue { get; init; }
    public string? SwatchWeave      { get; init; }
}

public sealed class DashboardResponse
{
    public DashboardStatsResponse                Stats        { get; init; } = new();
    public IReadOnlyList<SalesWeekResponse>      SalesChart   { get; init; } = [];
    public IReadOnlyList<BestSellerResponse>     BestSellers  { get; init; } = [];
    public IReadOnlyList<OrderSummaryResponse>   LatestOrders { get; init; } = [];
}
