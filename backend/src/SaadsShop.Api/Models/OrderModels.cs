namespace SaadsShop.Api.Models;

public class Customer
{
    public int       CustomerId  { get; set; }
    public string    Name        { get; set; } = string.Empty;

    /// <summary>Normalised to the local 03xxxxxxxxx form, and unique.</summary>
    public string    Phone       { get; set; } = string.Empty;

    public string?   Area        { get; set; }
    public string?   Address     { get; set; }
    public int       OrderCount  { get; set; }
    public decimal   TotalSpent  { get; set; }
    public DateTime? LastOrderAt { get; set; }
}

public class Order
{
    public int       OrderId         { get; set; }

    /// <summary>The number the shop and the customer both say out loud: SS-2419.</summary>
    public string    Reference       { get; set; } = string.Empty;

    public int       CustomerId      { get; set; }
    public string    CustomerName    { get; set; } = string.Empty;
    public string?   Phone           { get; set; }
    public string?   Area            { get; set; }

    public string    Status          { get; set; } = "Placed";
    public string    PaymentMethod   { get; set; } = "CashOnDelivery";

    public decimal   Subtotal        { get; set; }
    public decimal   DeliveryCharge  { get; set; }
    public decimal   Total           { get; set; }

    public string    DeliveryAddress { get; set; } = string.Empty;
    public string?   Notes           { get; set; }

    /// <summary>Comma-joined product names, built by the database for list views.</summary>
    public string?   ItemSummary     { get; set; }

    public int       LineCount       { get; set; }
    public DateTime  PlacedAt        { get; set; }
    public DateTime? UpdatedAt       { get; set; }
}

/// <summary>
/// Product name, cloth and unit price are snapshots taken at checkout, not
/// joins. An order is a historical record: renaming a product or raising its
/// price next season must not rewrite what a customer was charged.
/// </summary>
public class OrderLine
{
    public int     OrderLineId      { get; set; }
    public int     ProductId        { get; set; }
    public string  ProductName      { get; set; } = string.Empty;
    public int?    SwatchId         { get; set; }
    public string? SwatchName       { get; set; }
    public string? SwatchColorValue { get; set; }
    public string? SwatchWeave      { get; set; }
    public string? BedSize          { get; set; }
    public decimal UnitPrice        { get; set; }
    public int     Quantity         { get; set; }
    public decimal LineTotal        { get; set; }
}

public class OrderMeasurement
{
    public int      OrderMeasurementId { get; set; }
    public decimal? BedWidthIn         { get; set; }
    public decimal? BedLengthIn        { get; set; }
    public decimal? WindowDropIn       { get; set; }
    public int?     WindowCount        { get; set; }
    public string?  Notes              { get; set; }
    public string?  TakenBy            { get; set; }
    public DateTime TakenAt            { get; set; }
}

public class OrderStatusChange
{
    public string?  FromStatus { get; set; }
    public string   ToStatus   { get; set; } = string.Empty;
    public string?  Note       { get; set; }
    public string?  ChangedBy  { get; set; }
    public DateTime ChangedAt  { get; set; }
}

public class StitchingJob
{
    public int       StitchingJobId   { get; set; }
    public int       OrderId          { get; set; }
    public string    Reference        { get; set; } = string.Empty;
    public string    Title            { get; set; } = string.Empty;
    public string    Stage            { get; set; } = "Measuring";
    public string?   AssignedTo       { get; set; }
    public int?      SwatchId         { get; set; }
    public string?   SwatchColorValue { get; set; }
    public string?   SwatchWeave      { get; set; }
    public DateOnly? DueDate          { get; set; }
    public bool      IsOverdue        { get; set; }
}

public class ShopSettings
{
    public string    ShopName              { get; set; } = string.Empty;
    public string    City                  { get; set; } = string.Empty;
    public string    AddressLine           { get; set; } = string.Empty;
    public string    WhatsAppNumber        { get; set; } = string.Empty;
    public string?   BannerText            { get; set; }
    public string?   OpeningHours          { get; set; }
    public decimal   DeliveryCharge        { get; set; }
    public decimal   FreeDeliveryThreshold { get; set; }
    public bool      CashOnDeliveryEnabled { get; set; }
    public bool      WhatsAppOrdersEnabled { get; set; }
    public bool      ReserveInShopEnabled  { get; set; }
    public bool      CardPaymentEnabled    { get; set; }
    public DateTime? UpdatedAt             { get; set; }
    public string?   UpdatedBy             { get; set; }
}

/// <summary>The four stat tiles on the overview.</summary>
public class DashboardStats
{
    public decimal SalesToday                 { get; set; }
    public decimal SalesSameDayLastWeek       { get; set; }
    public int     OrdersOpen                 { get; set; }
    public int     OrdersAwaitingMeasurements { get; set; }
    public int     JobsOnFloor                { get; set; }
    public int     JobsDueTomorrow            { get; set; }
    public decimal MonthToDateSales           { get; set; }
}

public class SalesWeek
{
    public DateTime WeekStart  { get; set; }
    public decimal  Sales      { get; set; }
    public int      OrderCount { get; set; }
}

public class BestSeller
{
    public int     ProductId        { get; set; }
    public string  Name             { get; set; } = string.Empty;
    public int     SoldCount        { get; set; }
    public decimal Price            { get; set; }
    public decimal Revenue          { get; set; }
    public int?    DefaultSwatchId  { get; set; }
    public string? SwatchColorValue { get; set; }
    public string? SwatchWeave      { get; set; }
}
