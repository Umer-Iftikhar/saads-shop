namespace SaadsShop.Api.Configuration;

/// <summary>
/// Google OAuth credentials. Both blank means external sign-in is simply not
/// registered — the shop runs on password + 2FA alone, which is the sensible
/// default for a local shop that has not set Google up yet.
/// </summary>
public sealed class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";

    public string ClientId     { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
