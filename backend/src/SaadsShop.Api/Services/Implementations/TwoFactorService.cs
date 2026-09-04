using System.Security.Cryptography;
using System.Text;
using System.Web;
using OtpNet;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Services.Implementations;

/// <summary>
/// TOTP (RFC 6238) and recovery codes.
/// </summary>
public sealed class TwoFactorService(IConfiguration configuration) : ITwoFactorService
{
    private readonly string _issuer =
        configuration["Auth:TotpIssuer"] ?? "Saad's Shop";

    public string GenerateSecret()
        // 20 bytes = 160 bits, the size RFC 4226 specifies for HMAC-SHA1 TOTP
        // and what every authenticator app expects.
        => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public string BuildAuthenticatorUri(string email, string secret)
    {
        // The label carries issuer:account so the app shows "Saad's Shop
        // (nasir@…)" rather than a bare address the owner cannot place. Both
        // halves are escaped — an apostrophe in "Saad's Shop" would otherwise
        // break the URI.
        var issuer  = HttpUtility.UrlEncode(_issuer);
        var account = HttpUtility.UrlEncode(email);

        return $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}&digits=6&period=30";
    }

    public bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
            return false;

        // Authenticator apps show the code with a space; people paste it that way.
        code = code.Replace(" ", string.Empty).Trim();

        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(secret));

            // One step either side. Phones drift, and a person typing a code as
            // it rolls over should not be told they are wrong. Wider than this
            // starts meaningfully enlarging the guessing window.
            return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch (ArgumentException)
        {
            // A stored secret that is not valid base32 — corrupt or truncated.
            // Fail closed rather than throwing into the sign-in path.
            return false;
        }
    }

    public IReadOnlyList<string> GenerateRecoveryCodes(int count = 10)
    {
        var codes = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            // 5 bytes → 8 base32 characters, ~40 bits. Formatted xxxx-xxxx so a
            // person can read one off paper without losing their place.
            var bytes = RandomNumberGenerator.GetBytes(5);
            var text  = Base32Encoding.ToString(bytes).TrimEnd('=').ToLowerInvariant();

            codes.Add($"{text[..4]}-{text[4..8]}");
        }

        return codes;
    }

    /// <summary>
    /// SHA-256 over the normalised code. Same reasoning as refresh tokens:
    /// these are random, not chosen, so there is no dictionary to defend
    /// against and a slow KDF would buy nothing.
    /// </summary>
    public byte[] HashRecoveryCode(string code)
    {
        var normalised = code.Replace("-", string.Empty)
                             .Replace(" ", string.Empty)
                             .Trim()
                             .ToLowerInvariant();

        return SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
    }
}
