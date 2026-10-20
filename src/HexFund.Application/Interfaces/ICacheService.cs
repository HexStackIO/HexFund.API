namespace HexFund.Application.Interfaces;

public interface ICacheService
{
    /// <summary>
    /// Returns the cached value or builds and caches it. Null results are not cached.
    /// When <paramref name="accountId"/> is supplied the entry is evicted by
    /// <see cref="InvalidateAccount"/>. Callers must put the user id in the key for any
    /// per-user data.
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(
        string cacheKey,
        Func<Task<T?>> factory,
        TimeSpan duration,
        Guid? accountId = null) where T : class;

    /// <summary>Evicts every cached entry that was tagged with the account.</summary>
    void InvalidateAccount(Guid accountId);
}
