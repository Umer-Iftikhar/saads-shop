using System.Text.RegularExpressions;

namespace SaadsShop.Api.Common;

/// <summary>
/// Normalises Pakistani mobile numbers to the local 03xxxxxxxxx form.
/// </summary>
/// <remarks>
/// People write the same number a dozen ways — 0301 234 5678, 0301-234-5678,
/// +92 301 234 5678, 92 301 2345678. The shop identifies a customer by their
/// number, so all of those must collapse to one value or Hina Aslam becomes
/// four customers with one order each.
///
/// The database re-checks the final shape with its own CHECK constraint, so a
/// bug here cannot store a malformed number.
/// </remarks>
public static partial class PhoneNumber
{
    [GeneratedRegex(@"[\s\-\(\)\.]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^03\d{9}$")]
    private static partial Regex LocalForm();

    /// <summary>
    /// Returns the normalised number, or null when it is not a valid Pakistani
    /// mobile number.
    /// </summary>
    public static string? Normalise(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var digits = Separators().Replace(input.Trim(), string.Empty);

        // +923012345678 / 00923012345678 / 923012345678 → 03012345678
        if (digits.StartsWith("+92", StringComparison.Ordinal))
            digits = "0" + digits[3..];
        else if (digits.StartsWith("0092", StringComparison.Ordinal))
            digits = "0" + digits[4..];
        else if (digits.StartsWith("92", StringComparison.Ordinal) && digits.Length == 12)
            digits = "0" + digits[2..];
        else if (digits.StartsWith('3') && digits.Length == 10)
            // Typed without the leading zero, which people do constantly.
            digits = "0" + digits;

        return LocalForm().IsMatch(digits) ? digits : null;
    }

    public static bool IsValid(string? input) => Normalise(input) is not null;
}
