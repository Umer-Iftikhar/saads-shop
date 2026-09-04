namespace SaadsShop.Api.Services.Interfaces;

/// <summary>
/// A thin wrapper over <c>IMemoryCache</c> with versioned invalidation.
/// </summary>
/// <remarks>
/// IMemoryCache cannot enumerate or evict by prefix, so "clear the catalogue"
/// has no direct expression. Instead every catalogue key embeds a version
/// number: a write bumps the version, every old key becomes unreachable at
/// once, and the entries fall out on their own expiry. No scanning, no
/// bookkeeping, and no window where a stale entry can still be served after a
/// successful write.
/// </remarks>
public interface ICacheService
{
    /// <summary>Returns the cached value, or produces and caches it.</summary>
    Task<T> GetOrCreateAsync<T>(string key, TimeSpan lifetime, Func<Task<T>> factory);

    /// <summary>The current version counter for a family of keys.</summary>
    long GetVersion(string versionKey);

    /// <summary>
    /// Bumps a version, orphaning every key built from the old one. Called
    /// after a successful write, never before — bumping first would let a
    /// concurrent read repopulate the old version with pre-write data.
    /// </summary>
    void BumpVersion(string versionKey);

    void Remove(string key);
}
