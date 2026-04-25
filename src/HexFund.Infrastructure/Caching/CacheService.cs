using HexFund.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace HexFund.Infrastructure.Caching;

public class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheService> _logger;

    private static readonly Dictionary<string, TimeSpan> CacheDurations = new()
    {
        { "Accounts",        TimeSpan.FromMinutes(5) },
        { "Transactions",    TimeSpan.FromMinutes(2) },
        { "MonthlyOverview", TimeSpan.FromMinutes(3) },
        { "User",            TimeSpan.FromMinutes(10) }
    };

    public CacheService(IMemoryCache cache, ILogger<CacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public string AccountsListKey(Guid userId)                              => $"Accounts:User:{userId}";
    public string AccountKey(Guid accountId)                                => $"Account:{accountId}";
    public string TransactionsListKey(Guid accountId)                       => $"Transactions:Account:{accountId}";
    public string MonthlyOverviewKey(Guid accountId, int year, int month)   => $"MonthlyOverview:Account:{accountId}:Year:{year}:Month:{month}";
    public string TransactionsForDateKey(Guid accountId, DateTime date)     => $"TransactionsForDate:Account:{accountId}:Date:{date:yyyy-MM-dd}";
    public string MonthTransactionsKey(Guid accountId, int year, int month) => $"transactions:month:{accountId}:{year:D4}-{month:D2}";

    public async Task<T?> GetOrCreateAsync<T>(
        string cacheKey,
        Func<Task<T?>> factory,
        string category = "Default",
        TimeSpan? customDuration = null) where T : class
    {
        if (_cache.TryGetValue<T>(cacheKey, out var cached))
        {
            _logger.LogDebug("Cache hit: {CacheKey}", cacheKey);
            return cached;
        }

        _logger.LogDebug("Cache MISS for key: {CacheKey}", cacheKey);

        var value = await factory();

        if (value != null)
        {
            var duration = customDuration ??
                (CacheDurations.TryGetValue(category, out var catDuration)
                    ? catDuration
                    : TimeSpan.FromMinutes(5));

            var options = new MemoryCacheEntryOptions()
                .SetSize(1)
                .SetSlidingExpiration(duration)
                .SetAbsoluteExpiration(duration * 2);

            _cache.Set(cacheKey, value, options);
            _logger.LogDebug("Cached key: {CacheKey} for {Duration}", cacheKey, duration);
        }

        return value;
    }

    public void Invalidate(string cacheKey)
    {
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated cache key: {CacheKey}", cacheKey);
    }

    // ── Calendar month registry ───────────────────────────────────────────────────

    /// <summary>
    /// Tracks which (year, month) pairs have been cached for an account so that
    /// InvalidateCalendarData can evict them all atomically.
    /// </summary>
    public void RegisterCachedMonth(Guid accountId, int year, int month)
    {
        var setKey = MonthRegistryKey(accountId);
        var set = _cache.GetOrCreate(setKey, entry =>
        {
            entry.Priority = CacheItemPriority.NeverRemove;
            entry.Size = 1;
            return new HashSet<(int, int)>();
        })!;
        lock (set) set.Add((year, month));
    }

    /// <summary>
    /// Evicts all cached calendar data for the given account.
    /// Call after any transaction create, update, delete, or amend.
    /// </summary>
    public void InvalidateCalendarData(Guid accountId)
    {
        var setKey = MonthRegistryKey(accountId);
        if (!_cache.TryGetValue(setKey, out HashSet<(int year, int month)>? months) || months == null)
        {
            _logger.LogDebug("InvalidateCalendarData: no registered months for account {AccountId}", accountId);
            return;
        }

        HashSet<(int year, int month)> snapshot;
        lock (months) snapshot = new HashSet<(int, int)>(months);

        _cache.Remove(setKey);

        foreach (var (year, month) in snapshot)
        {
            Invalidate(MonthlyOverviewKey(accountId, year, month));
            Invalidate(MonthTransactionsKey(accountId, year, month));

            var daysInMonth = DateTime.DaysInMonth(year, month);
            for (int day = 1; day <= daysInMonth; day++)
                Invalidate(TransactionsForDateKey(accountId, new DateTime(year, month, day)));
        }

        _logger.LogDebug("Invalidated {MonthCount} months of calendar data for account {AccountId}",
            snapshot.Count, accountId);
    }

    private static string MonthRegistryKey(Guid accountId) => $"CalendarMonthRegistry:{accountId}";
}
