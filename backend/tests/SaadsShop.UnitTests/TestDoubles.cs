using Microsoft.Extensions.Logging.Abstractions;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.Models;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.UnitTests;

/// <summary>
/// A real cache, not a mock.
/// </summary>
/// <remarks>
/// The caching behaviour under test <em>is</em> the interaction between reads
/// and version bumps, so a substitute returning canned values would assert
/// nothing. This is the same versioned-key scheme the production service uses,
/// small enough to read in one sitting, and it records how many times a factory
/// actually ran — which is how a test tells a cache hit from a miss.
/// </remarks>
public sealed class FakeCache : ICacheService
{
    private readonly Dictionary<string, object?> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int>     _bumps   = new(StringComparer.Ordinal);

    /// <summary>How many times a factory has been invoked — one per cache miss.</summary>
    public int Misses { get; private set; }

    /// <summary>How many times a version has been bumped. What a test actually cares about.</summary>
    public int BumpCount(string versionKey) => _bumps.TryGetValue(versionKey, out var n) ? n : 0;

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan lifetime, Func<Task<T>> factory)
    {
        if (_entries.TryGetValue(key, out var cached)) return (T)cached!;

        Misses++;
        var value = await factory();
        _entries[key] = value;
        return value;
    }

    //  Starts at 1, like the real CacheService — a fake that starts somewhere
    //  else is a trap, because a test could pass here and fail in production.
    public long GetVersion(string versionKey) => 1 + BumpCount(versionKey);

    public void BumpVersion(string versionKey) => _bumps[versionKey] = BumpCount(versionKey) + 1;

    public void Remove(string key) => _entries.Remove(key);

    public bool Contains(string key) => _entries.ContainsKey(key);
}

/// <summary>Builders for the shapes a repository hands back.</summary>
public static class Given
{
    public static ProcedureResult<T> Ok<T>(T data, string message = "OK")
        => ProcedureResult<T>.From(data, new ProcedureStatus { ResponseCode = 200, ResponseMessage = message });

    public static ProcedureResult<T> Failed<T>(int code, string message)
        => ProcedureResult<T>.From(default, new ProcedureStatus { ResponseCode = code, ResponseMessage = message });

    /// <summary>A failure that nonetheless carries rows — services must not leak them.</summary>
    public static ProcedureResult<T> FailedWithData<T>(T data, int code, string message)
        => ProcedureResult<T>.From(data, new ProcedureStatus { ResponseCode = code, ResponseMessage = message });

    public static PageInfo Page(int totalCount = 1, int page = 1, int pageSize = 24, int needsAttention = 0)
        => new() { TotalCount = totalCount, Page = page, PageSize = pageSize, NeedsAttentionCount = needsAttention };

    public static Product AProduct(
        int id = 1, string name = "Gulaab Bridal Set", decimal price = 18_500m,
        int stock = 4, bool isActive = true) => new()
    {
        ProductId        = id,
        Name             = name,
        Slug             = name.ToLowerInvariant().Replace(' ', '-'),
        CategoryId       = 1,
        CategoryName     = "Wedding sets",
        CategorySlug     = "wedding-sets",
        Kicker           = "Bridal bedding",
        Blurb            = "Fourteen pieces.",
        Price            = price,
        Pieces           = "14 pieces",
        StitchingDays    = 3,
        Stock            = stock,
        LowStockAt       = 2,
        SoldCount        = 11,
        IsActive         = isActive,
        DefaultSwatchId  = 1,
        SwatchColorValue = "#c67139",
        SwatchWeave      = "Woven"
    };

    public static Order AnOrder(
        int id = 1, string reference = "SS-2419", string status = "Placed",
        decimal total = 18_800m) => new()
    {
        OrderId         = id,
        Reference       = reference,
        Status          = status,
        PaymentMethod   = "CashOnDelivery",
        Subtotal        = 18_500m,
        DeliveryCharge  = 300m,
        Total           = total,
        DeliveryAddress = "Shop 14, Moti Bazaar",
        CustomerName    = "Hina Aslam",
        Phone           = "03012345678",
        PlacedAt        = DateTime.UtcNow,
        LineCount       = 1,
        ItemSummary     = "Gulaab Bridal Set"
    };

    public static AppUser AUser(
        string id = "user-1", string email = "saad@saadsshop.pk",
        bool isActive = true, bool twoFactorEnabled = true,
        string[]? roles = null) => new()
    {
        Id                 = id,
        UserName           = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email              = email,
        NormalizedEmail    = email.ToUpperInvariant(),
        EmailConfirmed     = true,
        FullName           = "Saad",
        IsActive           = isActive,
        TwoFactorEnabled   = twoFactorEnabled,
        LockoutEnabled     = true,
        Roles              = roles ?? ["Owner"]
    };

    public static NullLogger<T> Log<T>() => NullLogger<T>.Instance;
}
