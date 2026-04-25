namespace HexFund.Application.Interfaces;

public interface ICacheService
{
    Task<T?> GetOrCreateAsync<T>(
        string cacheKey,
        Func<Task<T?>> factory,
        string category = "Default",
        TimeSpan? customDuration = null) where T : class;

    void Invalidate(string cacheKey);
    void RegisterCachedMonth(Guid accountId, int year, int month);
    void InvalidateCalendarData(Guid accountId);

    string AccountsListKey(Guid userId);
    string AccountKey(Guid accountId);
    string TransactionsListKey(Guid accountId);
    string MonthlyOverviewKey(Guid accountId, int year, int month);
    string TransactionsForDateKey(Guid accountId, DateTime date);
    string MonthTransactionsKey(Guid accountId, int year, int month);
}
