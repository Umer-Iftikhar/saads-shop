using System.ComponentModel.DataAnnotations;

namespace SaadsShop.Api.Configuration;

/// <summary>
/// JWT signing and lifetime settings. Validated at startup, so a deployment
/// with a missing or placeholder signing key refuses to come up rather than
/// quietly issuing tokens anyone could forge.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer   { get; init; } = string.Empty;
    [Required] public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// At least 32 bytes — HS256's key must be no shorter than its output, or
    /// the security margin the algorithm advertises is not there.
    /// </summary>
    [Required]
    [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters.")]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Short by design: the refresh token is what keeps a session alive, so a
    /// stolen access token is only useful for minutes.
    /// </summary>
    [Range(1, 120)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;

    /// <summary>Long enough to type a code from a phone, short enough to be useless if intercepted.</summary>
    [Range(1, 30)]
    public int TwoFactorChallengeMinutes { get; init; } = 5;

    /// <summary>
    /// How long one rotation's answer is replayed to callers presenting the same
    /// refresh token, instead of rotating again.
    /// </summary>
    /// <remarks>
    /// Refresh tokens rotate and a spent one is treated as theft, so two tabs
    /// refreshing at once — or one request retried after its response was lost —
    /// would revoke the whole family and sign the shop out. Within this window
    /// the second caller is handed the same tokens the first was.
    ///
    /// Seconds, not minutes: it covers a round trip, not a session. Set to 0 to
    /// switch it off and rely on the database alone.
    /// </remarks>
    [Range(0, 60)]
    public int RefreshRotationGraceSeconds { get; init; } = 20;

    /// <summary>
    /// Placeholders that must never reach production. Startup fails on any of
    /// them, because a key copied from a sample is the same as no key at all.
    /// </summary>
    public static readonly string[] ForbiddenKeys =
    [
        "change-me",
        "CHANGE_ME",
        "your-256-bit-secret",
        "supersecretkey",
        "development-only-signing-key-change-me"
    ];
}
