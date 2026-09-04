using System.ComponentModel.DataAnnotations;
using SaadsShop.Api.Validation;

namespace SaadsShop.Api.DTOs.Request;

/// <summary>One line of a cart at checkout.</summary>
/// <remarks>
/// Note what is absent: price and total. The client does not get a vote on
/// money — the checkout procedure reads the price from the products table under
/// lock. Anything money-shaped sent here would be ignored, so it is not
/// accepted in the first place.
/// </remarks>
public sealed class CartLineRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Unknown product.")]
    public int ProductId { get; init; }

    [Range(1, 999, ErrorMessage = "Quantity must be between 1 and 999.")]
    public int Quantity { get; init; } = 1;

    public int? SwatchId { get; init; }

    [RegularExpression("^(Single|Double|King)$", ErrorMessage = "Bed size must be Single, Double or King.")]
    public string? BedSize { get; init; }
}

public sealed class PlaceOrderRequest
{
    [Required(ErrorMessage = "Please tell us your name.")]
    [StringLength(128, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 128 characters.")]
    public string CustomerName { get; init; } = string.Empty;

    /// <summary>
    /// Pakistani mobile numbers, written the way people actually type them:
    /// 0301 234 5678, 0301-234-5678, +92 301 234 5678. Normalised to
    /// 03xxxxxxxxx before it reaches the database, which re-checks the shape.
    /// </summary>
    [Required(ErrorMessage = "A phone number is required.")]
    [RegularExpression(@"^(\+92|92|0)?[\s-]?3\d{2}[\s-]?\d{3}[\s-]?\d{4}$",
        ErrorMessage = "That phone number does not look right. Use the form 03xx xxx xxxx.")]
    public string Phone { get; init; } = string.Empty;

    [Required(ErrorMessage = "Please give an address in Rawalpindi.")]
    [StringLength(400, MinimumLength = 5, ErrorMessage = "Address must be between 5 and 400 characters.")]
    public string DeliveryAddress { get; init; } = string.Empty;

    [StringLength(96)] public string? Area { get; init; }

    [Required(ErrorMessage = "Choose how you would like to pay.")]
    [RegularExpression("^(CashOnDelivery|WhatsApp|ReserveInShop|Card)$",
        ErrorMessage = "Choose how you would like to pay.")]
    public string PaymentMethod { get; init; } = "CashOnDelivery";

    [StringLength(1000, ErrorMessage = "Notes must be 1000 characters or fewer.")]
    public string? Notes { get; init; }

    [Required(ErrorMessage = "Your cart is empty.")]
    [MinLength(1, ErrorMessage = "Your cart is empty.")]
    [MaxLength(50, ErrorMessage = "An order can hold at most 50 different items.")]
    public IReadOnlyList<CartLineRequest> Lines { get; init; } = [];
}

/// <summary>
/// Admin order search. The date range is validated three times over: here by
/// <see cref="DateRangeAttribute"/>, again by the stored procedure, and once
/// more in the browser before the request is sent.
/// </summary>
[DateRange(MaxSpanDays = 366, AllowFuture = false, AllowOpenRange = true)]
public sealed class OrderSearchQuery : IDateRange
{
    [RegularExpression("^(Placed|Measuring|Stitching|Ready|Delivered|Cancelled)$",
        ErrorMessage = "Unknown order status.")]
    public string? Status { get; init; }

    [StringLength(128, ErrorMessage = "Search text must be 128 characters or fewer.")]
    public string? Search { get; init; }

    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate   { get; init; }

    [Range(1, 10_000)] public int Page     { get; init; } = 1;
    [Range(1, 200)]    public int PageSize { get; init; } = 25;
}

public sealed class UpdateOrderStatusRequest
{
    [Required]
    [RegularExpression("^(Placed|Measuring|Stitching|Ready|Delivered|Cancelled)$",
        ErrorMessage = "That is not a status we use.")]
    public string Status { get; init; } = string.Empty;

    [StringLength(400)] public string? Note { get; init; }
}

public sealed class SaveMeasurementsRequest
{
    [Range(1, 200, ErrorMessage = "Bed width must be between 1 and 200 inches.")]
    public decimal? BedWidthIn { get; init; }

    [Range(1, 200, ErrorMessage = "Bed length must be between 1 and 200 inches.")]
    public decimal? BedLengthIn { get; init; }

    [Range(1, 300, ErrorMessage = "Window drop must be between 1 and 300 inches.")]
    public decimal? WindowDropIn { get; init; }

    [Range(0, 100, ErrorMessage = "Window count must be between 0 and 100.")]
    public int? WindowCount { get; init; }

    [StringLength(1000)] public string? Notes   { get; init; }
    [StringLength(128)]  public string? TakenBy { get; init; }
}

/// <summary>Customer-facing lookup: reference plus the phone it was placed with.</summary>
public sealed class TrackOrderQuery
{
    [Required(ErrorMessage = "An order number is required.")]
    [RegularExpression(@"^SS-\d{3,10}$", ErrorMessage = "Order numbers look like SS-2419.")]
    public string Reference { get; init; } = string.Empty;

    [Required(ErrorMessage = "The phone number on the order is required.")]
    [RegularExpression(@"^(\+92|92|0)?[\s-]?3\d{2}[\s-]?\d{3}[\s-]?\d{4}$",
        ErrorMessage = "That phone number does not look right.")]
    public string Phone { get; init; } = string.Empty;
}

/// <summary>Set-builder quote — bistar, parde and cushions in one bed size.</summary>
public sealed class SetBuilderQuoteRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Pick a cloth for the bistar.")]
    public int SheetProductId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Pick a cloth for the parde.")]
    public int CurtainProductId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Pick a cloth for the cushions.")]
    public int CushionProductId { get; init; }

    [Required(ErrorMessage = "Pick a bed size.")]
    [RegularExpression("^(Single|Double|King)$", ErrorMessage = "Bed size must be Single, Double or King.")]
    public string BedSize { get; init; } = "Double";
}
