using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace SaadsShop.Api.Validation;

/// <summary>
/// Validates a date-range request as a whole: the start must not be after the
/// end, neither may sit outside sensible bounds, and the span must not exceed a
/// limit.
/// </summary>
/// <remarks>
/// Applied to the <em>class</em>, not a property, because the rule is a
/// relationship between two values. A property-level attribute can only see its
/// own value and would have to reach back into the model by reflection to find
/// its partner — exactly the stringly-typed coupling that breaks silently when
/// someone renames a property.
///
/// Implement <see cref="IDateRange"/> and the attribute reads both ends through
/// the interface, so a rename becomes a compile error rather than a validator
/// that quietly stops validating.
///
/// This is the API-side check. Stored procedures re-check
/// <c>@FromDate &lt;= @ToDate</c> independently, and the browser validates
/// before submitting — three layers, deliberately.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class DateRangeAttribute : ValidationAttribute
{
    /// <summary>Widest span allowed, in days. A year of orders is a report, not a search.</summary>
    public int MaxSpanDays { get; init; } = 366;

    /// <summary>
    /// Reject a start date before this. Guards against a typo'd year ("0202")
    /// turning into a scan across the whole history.
    /// </summary>
    public int EarliestYear { get; init; } = 2000;

    /// <summary>
    /// Whether the range may extend past today. False for anything searching
    /// what has already happened; true for due dates and scheduling.
    /// </summary>
    public bool AllowFuture { get; init; }

    /// <summary>Whether both ends may be omitted, meaning "no date filter".</summary>
    public bool AllowOpenRange { get; init; } = true;

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not IDateRange range)
        {
            // Applied to a type that exposes no range: a wiring mistake, and
            // passing silently would hide it until a bad range reached SQL.
            return new ValidationResult(
                $"{nameof(DateRangeAttribute)} requires the model to implement {nameof(IDateRange)}.");
        }

        var from = range.FromDate;
        var to   = range.ToDate;

        if (from is null && to is null)
        {
            return AllowOpenRange
                ? ValidationResult.Success
                : Fail("Choose a date range.", nameof(IDateRange.FromDate));
        }

        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var earliest = new DateOnly(EarliestYear, 1, 1);
        var earliestText = earliest.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        if (from is { } f)
        {
            if (f < earliest)
                return Fail($"The start date must be on or after {earliestText}.", nameof(IDateRange.FromDate));

            if (!AllowFuture && f > today)
                return Fail("The start date cannot be in the future.", nameof(IDateRange.FromDate));
        }

        if (to is { } t)
        {
            if (t < earliest)
                return Fail($"The end date must be on or after {earliestText}.", nameof(IDateRange.ToDate));

            if (!AllowFuture && t > today)
                return Fail("The end date cannot be in the future.", nameof(IDateRange.ToDate));
        }

        if (from is { } start && to is { } end)
        {
            if (start > end)
                return Fail("The start date must be on or before the end date.", nameof(IDateRange.FromDate));

            // +1 because the range is inclusive at both ends: 1 Jan to 1 Jan is one day.
            var spanDays = end.DayNumber - start.DayNumber + 1;
            if (spanDays > MaxSpanDays)
                return Fail($"That range covers {spanDays:N0} days. Please choose {MaxSpanDays:N0} days or fewer.",
                            nameof(IDateRange.ToDate));
        }

        return ValidationResult.Success;
    }

    private static ValidationResult Fail(string message, string member) => new(message, [member]);
}

/// <summary>
/// Exposes a date range to <see cref="DateRangeAttribute"/>. Implemented by
/// every request that filters by date.
/// </summary>
public interface IDateRange
{
    DateOnly? FromDate { get; }
    DateOnly? ToDate   { get; }
}

/// <summary>
/// A single date that must not be in the future — "delivered on", "measured
/// on". Property-level, because unlike a range it needs no partner value.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotFutureDateAttribute : ValidationAttribute
{
    /// <summary>
    /// Tolerance for clock skew between a customer's phone and the server. A
    /// browser a few minutes fast should not produce an error nobody can act on.
    /// </summary>
    public int ToleranceMinutes { get; init; } = 10;

    public override bool IsValid(object? value)
    {
        if (value is null) return true;   // absence is [Required]'s business, not ours

        var limit = DateTime.UtcNow.AddMinutes(ToleranceMinutes);

        return value switch
        {
            DateOnly d       => d <= DateOnly.FromDateTime(limit),
            DateTime dt      => dt.ToUniversalTime() <= limit,
            DateTimeOffset o => o.UtcDateTime <= limit,
            _                => false
        };
    }

    public override string FormatErrorMessage(string name) => $"{name} cannot be in the future.";
}

/// <summary>
/// A date that must fall within a sensible window either side of today — a
/// stitching due date, say. Rejects a due date in 1970 and one in 2140 alike.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class ReasonableDateAttribute : ValidationAttribute
{
    public int MaxDaysInPast   { get; init; } = 365;
    public int MaxDaysInFuture { get; init; } = 365;

    public override bool IsValid(object? value)
    {
        if (value is null) return true;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        DateOnly? date = value switch
        {
            DateOnly d       => d,
            DateTime dt      => DateOnly.FromDateTime(dt),
            DateTimeOffset o => DateOnly.FromDateTime(o.UtcDateTime),
            _                => null
        };

        if (date is not { } d2) return false;

        return d2 >= today.AddDays(-MaxDaysInPast)
            && d2 <= today.AddDays(MaxDaysInFuture);
    }

    public override string FormatErrorMessage(string name)
        => $"{name} must be within {MaxDaysInPast} days before and {MaxDaysInFuture} days after today.";
}
