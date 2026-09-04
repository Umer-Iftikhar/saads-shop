namespace SaadsShop.Api.DTOs.Response;

/// <summary>
/// The answer to /auth/login. It is deliberately NOT an access token: the
/// password step alone never yields one, because every staff account carries
/// two-factor. What comes back is a short-lived token good only for the 2FA
/// endpoint.
/// </summary>
public sealed class LoginChallengeResponse
{
    public bool   RequiresTwoFactor { get; init; } = true;

    /// <summary>Valid for a few minutes and accepted by /auth/2fa/verify alone.</summary>
    public string MfaToken          { get; init; } = string.Empty;

    /// <summary>
    /// False when the account has not finished enrolling an authenticator, so
    /// the client can send them to setup rather than asking for a code they
    /// have no way to produce.
    /// </summary>
    public bool   IsTwoFactorEnrolled { get; init; }
}

/// <summary>
/// A signed-in session. The refresh token is absent by design — it is written
/// to an HttpOnly cookie and never handed to JavaScript, so an XSS bug cannot
/// read it.
/// </summary>
public sealed class AuthResponse
{
    public string   AccessToken  { get; init; } = string.Empty;
    public DateTime ExpiresAt    { get; init; }
    public string   TokenType    { get; init; } = "Bearer";

    public string   UserId       { get; init; } = string.Empty;
    public string   Email        { get; init; } = string.Empty;
    public string   FullName     { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed class CurrentUserResponse
{
    public string  UserId           { get; init; } = string.Empty;
    public string  Email            { get; init; } = string.Empty;
    public string  FullName         { get; init; } = string.Empty;
    public string? PhoneNumber      { get; init; }
    public bool    TwoFactorEnabled { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}

/// <summary>
/// Returned once, at enrolment. The secret is never retrievable afterwards —
/// an endpoint that could re-read it would turn any session hijack into a
/// permanent second factor bypass.
/// </summary>
public sealed class TwoFactorSetupResponse
{
    public string SharedKey   { get; init; } = string.Empty;

    /// <summary>otpauth:// URI for the authenticator's QR code.</summary>
    public string AuthenticatorUri { get; init; } = string.Empty;
}

/// <summary>Shown once and never again; the codes are stored only as hashes.</summary>
public sealed class RecoveryCodesResponse
{
    public IReadOnlyList<string> RecoveryCodes { get; init; } = [];
}

public sealed class StaffAccountResponse
{
    public string    Id                 { get; init; } = string.Empty;
    public string    FullName           { get; init; } = string.Empty;
    public string    Email              { get; init; } = string.Empty;
    public string?   PhoneNumber        { get; init; }
    public bool      TwoFactorEnabled   { get; init; }
    public bool      IsActive           { get; init; }
    public bool      IsLockedOut        { get; init; }
    public DateTime  CreatedAt          { get; init; }
    public IReadOnlyList<string> Roles  { get; init; } = [];
    public int       ExternalLoginCount { get; init; }
}
