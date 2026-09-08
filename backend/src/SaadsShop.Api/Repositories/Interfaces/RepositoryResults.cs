using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces;

/*  The shapes a procedure's several result sets are read into.

    They live together, above the query/command split, because a few are
    returned by both sides — placing an order returns the same order-with-lines
    that looking one up does — and duplicating them would let the two copies
    drift apart one field at a time.                                          */

/// <summary>A product with the cloths it can be made in and a few related items.</summary>
public sealed class ProductWithSwatches
{
    public Product?                 Product  { get; init; }
    public IReadOnlyList<Swatch>    Swatches { get; init; } = [];
    public IReadOnlyList<Product>   Related  { get; init; } = [];
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

public sealed class InventorySnapshot
{
    public IReadOnlyList<InventoryItem> Items         { get; init; } = [];
    public int                          ProductCount  { get; init; }
    public int                          LowStockCount { get; init; }
}

public sealed class DashboardData
{
    public DashboardStats             Stats        { get; init; } = new();
    public IReadOnlyList<SalesWeek>   SalesChart   { get; init; } = [];
    public IReadOnlyList<BestSeller>  BestSellers  { get; init; } = [];
    public IReadOnlyList<Order>       LatestOrders { get; init; } = [];
}

/// <summary>
/// A partial user update. Every field is nullable and null means "leave alone"
/// — Identity updates one facet at a time (a failed sign-in touches only the
/// failure count), and a full-row update would clobber concurrent changes.
/// </summary>
public sealed class UserUpdate
{
    public required string Id { get; init; }

    public string?  UserName           { get; init; }
    public string?  NormalizedUserName { get; init; }
    public string?  Email              { get; init; }
    public string?  NormalizedEmail    { get; init; }
    public bool?    EmailConfirmed     { get; init; }
    public string?  PasswordHash       { get; init; }
    public string?  SecurityStamp      { get; init; }
    public string?  ConcurrencyStamp   { get; init; }
    public string?  PhoneNumber        { get; init; }
    public string?  FullName           { get; init; }
    public bool?    TwoFactorEnabled   { get; init; }
    public DateTimeOffset? LockoutEnd  { get; init; }
    public bool     ClearLockout       { get; init; }
    public bool?    LockoutEnabled     { get; init; }
    public int?     AccessFailedCount  { get; init; }
    public bool?    IsActive           { get; init; }
}

public sealed class RefreshRedemption
{
    public string?  UserId         { get; set; }
    public string?  Email          { get; set; }
    public string?  FullName       { get; set; }
    public long?    RefreshTokenId { get; set; }
    public bool     ReuseDetected  { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
}
