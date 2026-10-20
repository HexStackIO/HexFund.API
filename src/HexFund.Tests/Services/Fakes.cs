using HexFund.Application.Interfaces;
using HexFund.Core.Entities;
using HexFund.Core.Interfaces;

namespace HexFund.Tests.Services;

internal sealed class FakeAccountRepository : IAccountRepository
{
    public List<Account> Accounts { get; } = new();

    public Task<Account?> GetByIdForUserAsync(Guid userId, Guid accountId) =>
        Task.FromResult(Accounts.FirstOrDefault(a => a.AccountId == accountId && a.UserId == userId));

    public Task<(IReadOnlyList<Account> Accounts, IReadOnlyList<Transaction> Transactions)>
        GetByUserIdWithTransactionsUpToDateAsync(Guid userId, DateTimeOffset upToDate) =>
        throw new NotSupportedException();

    public Task<Account> CreateAsync(Account account) { Accounts.Add(account); return Task.FromResult(account); }
    public Task<Account> UpdateAsync(Account account) => Task.FromResult(account);

    public Task<bool> DeleteForUserAsync(Guid userId, Guid accountId) =>
        Task.FromResult(Accounts.RemoveAll(a => a.AccountId == accountId && a.UserId == userId) > 0);

    public Task<bool> UserOwnsAccountAsync(Guid userId, Guid accountId) =>
        Task.FromResult(Accounts.Any(a => a.AccountId == accountId && a.UserId == userId));
}

internal sealed class FakeTransactionRepository : ITransactionRepository
{
    private readonly FakeAccountRepository _accounts;

    public FakeTransactionRepository(FakeAccountRepository accounts) => _accounts = accounts;

    public List<Transaction> Transactions { get; } = new();
    public int AmendCalls { get; private set; }
    public int QueryCalls { get; private set; }

    public Task<Transaction?> GetByIdForUserAsync(Guid transactionId, Guid userId) =>
        Task.FromResult(Transactions.FirstOrDefault(t =>
            t.TransactionId == transactionId
            && _accounts.Accounts.Any(a => a.AccountId == t.AccountId && a.UserId == userId)));

    public Task<IReadOnlyList<Transaction>> GetByAccountIdAsync(Guid accountId) =>
        Task.FromResult<IReadOnlyList<Transaction>>(Transactions.Where(t => t.AccountId == accountId).ToList());

    public Task<Transaction> CreateAsync(Transaction transaction) { Transactions.Add(transaction); return Task.FromResult(transaction); }
    public Task<Transaction> UpdateAsync(Transaction transaction) => Task.FromResult(transaction);

    public Task<bool> DeleteAsync(Guid transactionId) =>
        Task.FromResult(Transactions.RemoveAll(t => t.TransactionId == transactionId) > 0);

    public Task<bool> HasSuccessorAsync(Guid transactionId) =>
        Task.FromResult(Transactions.Any(t => t.PredecessorTransactionId == transactionId));

    public Task AmendAsync(Transaction original, Transaction successor)
    {
        AmendCalls++;
        Transactions.Add(successor);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Transaction>> GetByAccountAndDateRangeAsync(
        Guid accountId, DateTimeOffset startDate, DateTimeOffset endDate)
    {
        QueryCalls++;
        return Task.FromResult<IReadOnlyList<Transaction>>(Transactions
            .Where(t => t.AccountId == accountId && t.IsActive && t.StartDate <= endDate
                        && (!t.EndDate.HasValue || t.EndDate >= startDate))
            .ToList());
    }

    public Task<IReadOnlyList<Transaction>> GetByAccountUpToDateAsync(Guid accountId, DateTimeOffset upToDate)
    {
        QueryCalls++;
        return Task.FromResult<IReadOnlyList<Transaction>>(Transactions
            .Where(t => t.AccountId == accountId && t.IsActive && t.StartDate <= upToDate)
            .ToList());
    }
}

internal sealed class RecordingCache : ICacheService
{
    public List<Guid> InvalidatedAccounts { get; } = new();
    private readonly Dictionary<string, object> _entries = new();

    public async Task<T?> GetOrCreateAsync<T>(string cacheKey, Func<Task<T?>> factory, TimeSpan duration, Guid? accountId = null)
        where T : class
    {
        if (_entries.TryGetValue(cacheKey, out var hit))
            return (T)hit;

        var value = await factory();
        if (value != null)
            _entries[cacheKey] = value;
        return value;
    }

    public void InvalidateAccount(Guid accountId)
    {
        InvalidatedAccounts.Add(accountId);
        _entries.Clear();
    }
}
