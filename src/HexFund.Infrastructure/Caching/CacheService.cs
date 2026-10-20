using System.Collections.Concurrent;
using HexFund.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace HexFund.Infrastructure.Caching;

/// <summary>
/// IMemoryCache wrapper with per-account eviction. Each account owns a cancellation token;
/// every entry tagged with that account expires when the token is cancelled, so
/// invalidation never has to enumerate keys. Registered as a singleton.
/// </summary>
public class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _accountTokens = new();

    public CacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<T?> GetOrCreateAsync<T>(
        string cacheKey,
        Func<Task<T?>> factory,
        TimeSpan duration,
        Guid? accountId = null) where T : class
    {
        if (_cache.TryGetValue(cacheKey, out T? cached))
            return cached;

        // Take the token before running the factory: if the account is invalidated while
        // the factory is running, the (possibly stale) result expires immediately.
        CancellationToken? token = accountId.HasValue ? GetToken(accountId.Value) : null;

        var value = await factory();
        if (value == null)
            return null;

        var options = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetAbsoluteExpiration(duration);

        if (token.HasValue)
            options.AddExpirationToken(new CancellationChangeToken(token.Value));

        _cache.Set(cacheKey, value, options);
        return value;
    }

    public void InvalidateAccount(Guid accountId)
    {
        if (_accountTokens.TryRemove(accountId, out var source))
        {
            source.Cancel();
            source.Dispose();
        }
    }

    private CancellationToken GetToken(Guid accountId)
    {
        while (true)
        {
            var source = _accountTokens.GetOrAdd(accountId, _ => new CancellationTokenSource());
            try
            {
                return source.Token;
            }
            catch (ObjectDisposedException)
            {
                // Lost a race with InvalidateAccount; retry with a fresh source.
                _accountTokens.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(accountId, source));
            }
        }
    }
}
