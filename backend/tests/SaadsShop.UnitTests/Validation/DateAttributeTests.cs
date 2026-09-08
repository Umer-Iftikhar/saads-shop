using SaadsShop.Api.Validation;

namespace SaadsShop.UnitTests.Validation;

public class NotFutureDateAttributeTests
{
    private static readonly NotFutureDateAttribute Attribute = new();

    [Fact]
    public void Null_is_left_to_Required()
        => Assert.True(Attribute.IsValid(null));

    [Fact]
    public void Yesterday_passes()
        => Assert.True(Attribute.IsValid(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))));

    [Fact]
    public void Tomorrow_is_refused()
        => Assert.False(Attribute.IsValid(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))));

    [Fact]
    public void A_clock_a_few_minutes_fast_is_tolerated()
        => Assert.True(Attribute.IsValid(DateTime.UtcNow.AddMinutes(5)));

    [Fact]
    public void A_clock_beyond_the_tolerance_is_not()
        => Assert.False(Attribute.IsValid(DateTime.UtcNow.AddMinutes(30)));

    [Fact]
    public void The_tolerance_is_configurable()
    {
        var generous = new NotFutureDateAttribute { ToleranceMinutes = 60 };
        Assert.True(generous.IsValid(DateTime.UtcNow.AddMinutes(30)));
    }

    [Fact]
    public void Accepts_DateTimeOffset_and_compares_in_utc()
    {
        var justPassed = new DateTimeOffset(DateTime.UtcNow.AddMinutes(-1), TimeSpan.Zero);
        Assert.True(Attribute.IsValid(justPassed));
        Assert.False(Attribute.IsValid(justPassed.AddHours(5)));
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData(42)]
    [InlineData(true)]
    public void A_value_that_is_not_a_date_fails_rather_than_passing(object value)
        => Assert.False(Attribute.IsValid(value));

    [Fact]
    public void The_message_names_the_field()
        => Assert.Equal("Delivered on cannot be in the future.", Attribute.FormatErrorMessage("Delivered on"));
}

public class ReasonableDateAttributeTests
{
    private static readonly ReasonableDateAttribute Attribute = new();
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public void Null_is_left_to_Required()
        => Assert.True(Attribute.IsValid(null));

    [Fact]
    public void Today_is_reasonable()
        => Assert.True(Attribute.IsValid(Today));

    [Theory]
    [InlineData(-364)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(364)]
    public void Dates_inside_the_window_pass(int offsetDays)
        => Assert.True(Attribute.IsValid(Today.AddDays(offsetDays)));

    [Theory]
    [InlineData(-366)]
    [InlineData(-3650)]
    [InlineData(366)]
    [InlineData(36500)]
    public void Dates_outside_the_window_are_refused(int offsetDays)
        => Assert.False(Attribute.IsValid(Today.AddDays(offsetDays)));

    [Fact]
    public void A_due_date_in_1970_and_one_in_2140_are_both_refused()
    {
        Assert.False(Attribute.IsValid(new DateOnly(1970, 1, 1)));
        Assert.False(Attribute.IsValid(new DateOnly(2140, 1, 1)));
    }

    [Fact]
    public void The_window_is_configurable_in_each_direction()
    {
        var stitching = new ReasonableDateAttribute { MaxDaysInPast = 7, MaxDaysInFuture = 90 };

        Assert.True(stitching.IsValid(Today.AddDays(-7)));
        Assert.False(stitching.IsValid(Today.AddDays(-8)));
        Assert.True(stitching.IsValid(Today.AddDays(90)));
        Assert.False(stitching.IsValid(Today.AddDays(91)));
    }

    [Fact]
    public void Accepts_the_three_date_types()
    {
        Assert.True(Attribute.IsValid(Today));
        Assert.True(Attribute.IsValid(DateTime.UtcNow));
        Assert.True(Attribute.IsValid(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_value_that_is_not_a_date_fails()
        => Assert.False(Attribute.IsValid("2026-01-01"));
}
