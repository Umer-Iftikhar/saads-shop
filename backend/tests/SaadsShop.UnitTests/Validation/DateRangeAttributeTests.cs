using System.ComponentModel.DataAnnotations;
using SaadsShop.Api.Validation;

namespace SaadsShop.UnitTests.Validation;

/// <summary>
/// The custom attribute behind every date search. It is the middle of three
/// layers — the browser checks first for feedback, the stored procedure checks
/// last for safety — and this is the one that decides what the API accepts.
/// </summary>
public class DateRangeAttributeTests
{
    /// <summary>A minimal model implementing the interface the attribute reads through.</summary>
    private sealed class Range(DateOnly? from, DateOnly? to) : IDateRange
    {
        public DateOnly? FromDate { get; } = from;
        public DateOnly? ToDate   { get; } = to;
    }

    private sealed class NotARange;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static ValidationResult? Validate(
        object model, int maxSpanDays = 366, bool allowFuture = false,
        bool allowOpenRange = true, int earliestYear = 2000)
    {
        var attribute = new DateRangeAttribute
        {
            MaxSpanDays    = maxSpanDays,
            AllowFuture    = allowFuture,
            AllowOpenRange = allowOpenRange,
            EarliestYear   = earliestYear
        };

        return attribute.GetValidationResult(model, new ValidationContext(model));
    }

    // ── the happy shapes ─────────────────────────────────────────────────────

    [Fact]
    public void An_ordinary_range_passes()
        => Assert.Null(Validate(new Range(Today.AddDays(-30), Today)));

    [Fact]
    public void Both_ends_omitted_means_no_filter_and_passes_by_default()
        => Assert.Null(Validate(new Range(null, null)));

    [Fact]
    public void Only_a_start_passes()
        => Assert.Null(Validate(new Range(Today.AddDays(-7), null)));

    [Fact]
    public void Only_an_end_passes()
        => Assert.Null(Validate(new Range(null, Today)));

    [Fact]
    public void A_single_day_passes_because_the_range_is_inclusive()
        => Assert.Null(Validate(new Range(Today, Today)));

    // ── ordering ─────────────────────────────────────────────────────────────

    [Fact]
    public void Backwards_range_is_refused_and_blamed_on_the_start_date()
    {
        var result = Validate(new Range(Today, Today.AddDays(-1)));

        Assert.NotNull(result);
        Assert.Equal("The start date must be on or before the end date.", result!.ErrorMessage);
        Assert.Equal([nameof(IDateRange.FromDate)], result.MemberNames);
    }

    // ── the span limit ───────────────────────────────────────────────────────

    [Fact]
    public void A_span_exactly_at_the_limit_passes()
    {
        // Inclusive at both ends, so N days is N-1 days of difference.
        var result = Validate(new Range(Today.AddDays(-364), Today), maxSpanDays: 365);
        Assert.Null(result);
    }

    [Fact]
    public void One_day_past_the_limit_is_refused_and_blamed_on_the_end_date()
    {
        var result = Validate(new Range(Today.AddDays(-365), Today), maxSpanDays: 365);

        Assert.NotNull(result);
        Assert.Contains("366 days", result!.ErrorMessage);
        Assert.Contains("365 days or fewer", result.ErrorMessage);
        Assert.Equal([nameof(IDateRange.ToDate)], result.MemberNames);
    }

    [Fact]
    public void The_customer_screens_wider_limit_admits_five_years()
        => Assert.Null(Validate(new Range(Today.AddDays(-1829), Today), maxSpanDays: 1830));

    // ── the future ───────────────────────────────────────────────────────────

    [Fact]
    public void A_future_start_is_refused_when_the_search_looks_backwards()
    {
        var result = Validate(new Range(Today.AddDays(1), Today.AddDays(2)));

        Assert.NotNull(result);
        Assert.Equal("The start date cannot be in the future.", result!.ErrorMessage);
        Assert.Equal([nameof(IDateRange.FromDate)], result.MemberNames);
    }

    [Fact]
    public void A_future_end_is_refused_when_the_search_looks_backwards()
    {
        var result = Validate(new Range(Today.AddDays(-1), Today.AddDays(1)));

        Assert.NotNull(result);
        Assert.Equal("The end date cannot be in the future.", result!.ErrorMessage);
        Assert.Equal([nameof(IDateRange.ToDate)], result.MemberNames);
    }

    [Fact]
    public void Today_is_not_the_future()
        => Assert.Null(Validate(new Range(Today, Today)));

    [Fact]
    public void A_future_range_passes_where_scheduling_allows_it()
        => Assert.Null(Validate(new Range(Today.AddDays(3), Today.AddDays(10)), allowFuture: true));

    // ── the floor ────────────────────────────────────────────────────────────

    [Fact]
    public void A_typo_year_is_refused_rather_than_scanning_the_whole_history()
    {
        var result = Validate(new Range(new DateOnly(202, 1, 1), Today));

        Assert.NotNull(result);
        Assert.Contains("1 Jan 2000", result!.ErrorMessage);
        Assert.Equal([nameof(IDateRange.FromDate)], result.MemberNames);
    }

    [Fact]
    public void An_end_date_below_the_floor_is_blamed_on_the_end_date()
    {
        var result = Validate(new Range(null, new DateOnly(1999, 12, 31)));

        Assert.NotNull(result);
        Assert.Equal([nameof(IDateRange.ToDate)], result.MemberNames);
    }

    [Fact]
    public void The_first_day_of_the_earliest_year_is_allowed()
        => Assert.Null(Validate(new Range(new DateOnly(2000, 1, 1), Today), maxSpanDays: int.MaxValue));

    [Fact]
    public void The_floor_is_configurable()
    {
        Assert.NotNull(Validate(new Range(new DateOnly(2019, 6, 1), Today),
                                maxSpanDays: int.MaxValue, earliestYear: 2020));
        Assert.Null(Validate(new Range(new DateOnly(2020, 6, 1), Today),
                             maxSpanDays: int.MaxValue, earliestYear: 2020));
    }

    // ── open ranges where a filter is compulsory ─────────────────────────────

    [Fact]
    public void An_empty_range_is_refused_where_a_range_is_required()
    {
        var result = Validate(new Range(null, null), allowOpenRange: false);

        Assert.NotNull(result);
        Assert.Equal("Choose a date range.", result!.ErrorMessage);
        Assert.Equal([nameof(IDateRange.FromDate)], result.MemberNames);
    }

    // ── wiring mistakes ──────────────────────────────────────────────────────

    [Fact]
    public void Applied_to_a_model_with_no_range_it_complains_rather_than_passing_silently()
    {
        var result = Validate(new NotARange());

        Assert.NotNull(result);
        Assert.Contains(nameof(IDateRange), result!.ErrorMessage);
    }

    // ── ordering of the checks ───────────────────────────────────────────────

    [Fact]
    public void A_range_that_breaks_several_rules_reports_the_start_date_first()
    {
        // Below the floor AND backwards AND too wide.
        var result = Validate(new Range(new DateOnly(1900, 1, 1), new DateOnly(1899, 1, 1)));

        Assert.NotNull(result);
        Assert.Contains("start date must be on or after", result!.ErrorMessage);
    }
}
