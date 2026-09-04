namespace SaadsShop.Api.Constants;

/// <summary>
/// Named rate-limit policies. Configured in Program.cs and applied per endpoint
/// with [EnableRateLimiting].
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Sign-in: 5 attempts per 15 minutes. Partitioned by IP and by the email
    /// being tried, so one attacker cannot lock out a whole shop by hammering
    /// one address, and cannot spread a guessing run across many addresses from
    /// one connection either.
    /// </summary>
    public const string Login = "login";

    /// <summary>
    /// A six-digit code is one in a million; unlimited guessing walks straight
    /// through it. 5 per 15 minutes per account.
    /// </summary>
    public const string TwoFactor = "two-factor";

    /// <summary>Placing orders: 10 per hour per IP.</summary>
    public const string PlaceOrder = "place-order";

    /// <summary>Order lookup: references are guessable, so 20 per 15 minutes per IP.</summary>
    public const string TrackOrder = "track-order";

    /// <summary>Everything else: 100 per minute.</summary>
    public const string Default = "default";
}
