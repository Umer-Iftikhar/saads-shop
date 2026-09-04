namespace SaadsShop.Api.Constants;

/// <summary>
/// Cache keys and lifetimes. Keys are versioned: a write bumps a counter rather
/// than trying to enumerate and evict matching entries, which IMemoryCache
/// cannot do anyway. See docs/architecture.md.
/// </summary>
public static class CacheKeys
{
    public const string CatalogVersion  = "catalog:version";
    public const string SettingsVersion = "settings:version";

    public static string ProductList(long version, string fingerprint)
        => $"catalog:products:v{version}:{fingerprint}";

    public static string Product(long version, string key)
        => $"catalog:product:v{version}:{key}";

    public static string Categories(long version) => $"catalog:categories:v{version}";
    public static string Swatches(long version)   => $"catalog:swatches:v{version}";
    public static string BedSizes(long version)   => $"catalog:bedsizes:v{version}";

    public static string PublicSettings(long version) => $"settings:public:v{version}";

    public static string Dashboard(DateOnly day) => $"dashboard:{day:yyyy-MM-dd}";

    public static class Lifetimes
    {
        public static readonly TimeSpan Catalog   = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan Reference = TimeSpan.FromHours(1);
        public static readonly TimeSpan Settings  = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Short: the overview is the screen Saad watches while orders come in,
        /// and stale sales figures there are worse than a little more load.
        /// </summary>
        public static readonly TimeSpan Dashboard = TimeSpan.FromMinutes(2);
    }
}
