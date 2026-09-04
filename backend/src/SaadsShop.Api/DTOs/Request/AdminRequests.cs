using System.ComponentModel.DataAnnotations;
using SaadsShop.Api.Validation;

namespace SaadsShop.Api.DTOs.Request;

public sealed class InventorySearchQuery
{
    [StringLength(128, ErrorMessage = "Search text must be 128 characters or fewer.")]
    public string? Search { get; init; }

    public bool LowStockOnly { get; init; }
}

public sealed class AdjustStockRequest
{
    /// <summary>
    /// Signed: negative removes stock. Zero is rejected — an adjustment that
    /// changes nothing is a mistake worth surfacing, not a no-op to absorb.
    /// </summary>
    [Range(-100_000, 100_000, ErrorMessage = "The adjustment must be between -100,000 and 100,000.")]
    public int Delta { get; init; }

    [Required(ErrorMessage = "Give a reason for the adjustment.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "The reason must be between 3 and 200 characters.")]
    public string Reason { get; init; } = string.Empty;
}

public sealed class StitchingJobCreateRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Unknown order.")]
    public int OrderId { get; init; }

    [Required(ErrorMessage = "Give the job a description.")]
    [StringLength(160, MinimumLength = 2, ErrorMessage = "The description must be between 2 and 160 characters.")]
    public string Title { get; init; } = string.Empty;

    [StringLength(128)] public string? AssignedTo { get; init; }

    public int? SwatchId    { get; init; }
    public int? OrderLineId { get; init; }

    /// <summary>Due dates are scheduling, so the future is allowed — within reason.</summary>
    [ReasonableDate(MaxDaysInPast = 30, MaxDaysInFuture = 365)]
    public DateOnly? DueDate { get; init; }
}

public sealed class StitchingJobUpdateRequest
{
    [RegularExpression("^(Measuring|Cutting|Stitching|Ready|Done)$",
        ErrorMessage = "That is not a stage on the floor.")]
    public string? Stage { get; init; }

    [StringLength(128)] public string? AssignedTo { get; init; }

    [ReasonableDate(MaxDaysInPast = 30, MaxDaysInFuture = 365)]
    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// Explicit, because a null DueDate means "leave it alone". Without this
    /// flag there would be no way to express "remove the due date".
    /// </summary>
    public bool ClearDueDate { get; init; }
}

/// <summary>
/// Customer search. Carries a date range over when they last ordered, so it is
/// validated by the same custom attribute as the order search.
/// </summary>
[DateRange(MaxSpanDays = 1830, AllowFuture = false, AllowOpenRange = true)]
public sealed class CustomerSearchQuery : IDateRange
{
    [StringLength(128, ErrorMessage = "Search text must be 128 characters or fewer.")]
    public string? Search { get; init; }

    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate   { get; init; }

    [Range(1, 10_000)] public int Page     { get; init; } = 1;
    [Range(1, 200)]    public int PageSize { get; init; } = 25;
}

/// <summary>
/// Dashboard "as at" date. Injectable so the overview can be reviewed for a
/// past day, and so tests are not at the mercy of the clock.
/// </summary>
public sealed class DashboardQuery
{
    [NotFutureDate]
    public DateOnly? AsAt { get; init; }
}

public sealed class SettingsUpdateRequest
{
    [Required(ErrorMessage = "The shop needs a name.")]
    [StringLength(128, MinimumLength = 2)]
    public string ShopName { get; init; } = string.Empty;

    [Required(ErrorMessage = "City is required.")]
    [StringLength(64)]
    public string City { get; init; } = string.Empty;

    [Required(ErrorMessage = "Address is required.")]
    [StringLength(256, MinimumLength = 5)]
    public string AddressLine { get; init; } = string.Empty;

    [Required(ErrorMessage = "A WhatsApp number is required.")]
    [RegularExpression(@"^(\+92|92|0)?[\s-]?3\d{2}[\s-]?\d{3}[\s-]?\d{4}$",
        ErrorMessage = "The WhatsApp number should look like 03xx xxx xxxx.")]
    public string WhatsAppNumber { get; init; } = string.Empty;

    [StringLength(400)] public string? BannerText   { get; init; }
    [StringLength(200)] public string? OpeningHours { get; init; }

    [Range(0, 100_000, ErrorMessage = "Delivery charge must be between Rs 0 and Rs 100,000.")]
    public decimal DeliveryCharge { get; init; }

    [Range(0, 10_000_000, ErrorMessage = "The free-delivery threshold is out of range.")]
    public decimal FreeDeliveryThreshold { get; init; }

    public bool CashOnDeliveryEnabled { get; init; }
    public bool WhatsAppOrdersEnabled { get; init; }
    public bool ReserveInShopEnabled  { get; init; }
    public bool CardPaymentEnabled    { get; init; }
}
