namespace SaadsShop.Api.Services.Interfaces;

public interface ITwoFactorService
{
    /// <summary>A new base32 TOTP secret.</summary>
    string GenerateSecret();

    /// <summary>The otpauth:// URI an authenticator app scans.</summary>
    string BuildAuthenticatorUri(string email, string secret);

    /// <summary>
    /// Verifies a six-digit code against the secret, allowing one 30-second
    /// step either side for clock drift between the phone and the server.
    /// </summary>
    bool VerifyCode(string secret, string code);

    /// <summary>Ten single-use recovery codes, returned in plain text exactly once.</summary>
    IReadOnlyList<string> GenerateRecoveryCodes(int count = 10);

    byte[] HashRecoveryCode(string code);
}
