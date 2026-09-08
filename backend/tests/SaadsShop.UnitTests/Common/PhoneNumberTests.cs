using SaadsShop.Api.Common;

namespace SaadsShop.UnitTests.Common;

/// <summary>
/// The shop identifies a customer by their phone number, so every spelling of
/// one number has to collapse to a single value — otherwise Hina Aslam becomes
/// four customers with one order each.
/// </summary>
public class PhoneNumberTests
{
    [Theory]
    // already local
    [InlineData("03012345678",        "03012345678")]
    // separators people actually type
    [InlineData("0301 234 5678",      "03012345678")]
    [InlineData("0301-234-5678",      "03012345678")]
    [InlineData("(0301) 2345678",     "03012345678")]
    [InlineData("0301.234.5678",      "03012345678")]
    [InlineData("  03012345678  ",    "03012345678")]
    // international spellings
    [InlineData("+923012345678",      "03012345678")]
    [InlineData("+92 301 234 5678",   "03012345678")]
    [InlineData("00923012345678",     "03012345678")]
    [InlineData("923012345678",       "03012345678")]
    [InlineData("92 301 2345678",     "03012345678")]
    // written without the leading zero
    [InlineData("3012345678",         "03012345678")]
    [InlineData("301 234 5678",       "03012345678")]
    public void Normalises_every_spelling_to_the_local_form(string input, string expected)
        => Assert.Equal(expected, PhoneNumber.Normalise(input));

    [Fact]
    public void All_spellings_of_one_number_collapse_to_the_same_value()
    {
        string[] spellings =
        [
            "03451112233", "0345 111 2233", "0345-111-2233",
            "+923451112233", "00923451112233", "923451112233", "3451112233"
        ];

        var normalised = spellings.Select(PhoneNumber.Normalise).Distinct().ToList();

        Assert.Single(normalised);
        Assert.Equal("03451112233", normalised[0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0301234567")]      // one digit short
    [InlineData("030123456789")]    // one digit long
    [InlineData("04012345678")]     // landline prefix, not a mobile
    [InlineData("02012345678")]
    [InlineData("13012345678")]     // does not start 03
    [InlineData("+913012345678")]   // India, not Pakistan
    [InlineData("abcdefghijk")]
    [InlineData("0301234567a")]
    [InlineData("+92301234567")]    // short after the country code
    public void Rejects_what_is_not_a_pakistani_mobile(string? input)
    {
        Assert.Null(PhoneNumber.Normalise(input));
        Assert.False(PhoneNumber.IsValid(input));
    }

    [Theory]
    [InlineData("03012345678")]
    [InlineData("+92 301 234 5678")]
    public void IsValid_agrees_with_Normalise(string input)
        => Assert.True(PhoneNumber.IsValid(input));

    [Fact]
    public void Normalising_an_already_normalised_number_changes_nothing()
    {
        var once  = PhoneNumber.Normalise("+92 301 234 5678");
        var twice = PhoneNumber.Normalise(once);

        Assert.Equal(once, twice);
    }

    [Theory]
    [InlineData("03002345678")]   // every live mobile prefix in Pakistan
    [InlineData("03112345678")]
    [InlineData("03212345678")]
    [InlineData("03312345678")]
    [InlineData("03412345678")]
    public void Accepts_each_network_prefix(string input)
        => Assert.Equal(input, PhoneNumber.Normalise(input));
}
