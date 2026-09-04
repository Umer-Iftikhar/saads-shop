namespace SaadsShop.Api.Constants;

public static class Roles
{
    public const string Owner = "Owner";
    public const string Staff = "Staff";
}

public static class AuthPolicies
{
    /// <summary>Any signed-in shop account.</summary>
    public const string StaffOnly = nameof(StaffOnly);

    /// <summary>Settings, staff management, product deletion.</summary>
    public const string OwnerOnly = nameof(OwnerOnly);

    /// <summary>
    /// Requires that two-factor was actually performed for this token, read
    /// from the <c>amr</c> claim rather than a flag on the user record — a
    /// token minted before 2FA was enrolled must not satisfy it.
    /// </summary>
    public const string MfaVerified = nameof(MfaVerified);
}

/// <summary>Claim types beyond the JWT registered set.</summary>
public static class AppClaims
{
    /// <summary>
    /// Authentication Methods References (RFC 8176): how the holder actually
    /// authenticated — "pwd", "mfa", "google".
    /// </summary>
    public const string AuthMethod = "amr";

    public const string FullName = "name";

    /// <summary>Marks the short-lived token that only /auth/2fa/verify accepts.</summary>
    public const string TokenPurpose = "purpose";
}

public static class AuthMethods
{
    public const string Password    = "pwd";
    public const string TwoFactor   = "mfa";
    public const string Google      = "google";
    public const string RecoveryCode = "rc";
}

public static class TokenPurposes
{
    /// <summary>A full session token.</summary>
    public const string Access = "access";

    /// <summary>
    /// Issued after the password step and accepted by the 2FA endpoint alone.
    /// Its narrow purpose is what stops a half-authenticated caller reaching
    /// anything else.
    /// </summary>
    public const string TwoFactorChallenge = "2fa";
}

/// <summary>Identity token-store coordinates for the authenticator secret.</summary>
public static class TokenStore
{
    public const string Provider            = "[AspNetUserStore]";
    public const string AuthenticatorKey    = "AuthenticatorKey";
}
