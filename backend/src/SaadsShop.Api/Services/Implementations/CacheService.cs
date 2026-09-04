using Microsoft.Extensions.Caching.Memory;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Services.Implementations;

/// <inheritdoc />
public sealed class CacheService(IMemoryCache cache) : ICacheService
{
    /// <summary>
    /// Serialises concurrent misses on the same key so a cold cache under load
    /// runs the query once rather than once per waiting request. Keyed
    /// semaphores rather than one global lock: two different keys should never
    /// wait on each other.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan lifetime, Func<Task<T>> factory)
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try
        {
            // Re-check: another request may have populated it while we queued.
            if (cache.TryGetValue(key, out cached) && cached is not null)
                return cached;

            var value = await factory();

            cache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lifetime,

                // Every entry declares a size because the cache is created with
                // a SizeLimit. Without one, Set throws — and with an unbounded
                // cache a large catalogue could exhaust memory instead.
                Size = 1
            });

            return value;
        }
        finally
        {
            gate.Release();

            // Drop the semaphore once nobody is waiting, so the dictionary does
            // not grow one entry per distinct cache key seen since startup.
            if (gate.CurrentCount == 1) Locks.TryRemove(key, out _);
        }
    }

    public long GetVersion(string versionKey)
        => cache.GetOrCreate(versionKey, entry =>
        {
            // Versions never expire on their own; they change only when a write
            // bumps them. An expiring version would silently orphan live keys.
            entry.Priority = CacheItemPriority.NeverRemove;
            entry.Size     = 1;
            return 1L;
        });

    public void BumpVersion(string versionKey)
    {
        var next = GetVersion(versionKey) + 1;

        cache.Set(versionKey, next, new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.NeverRemove,
            Size     = 1
        });
    }

    public void Remove(string key) => cache.Remove(key);
}
