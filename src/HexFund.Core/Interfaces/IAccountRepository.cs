using HexFund.Core.Entities;

namespace HexFund.Core.Interfaces;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid accountId);
    Task<IEnumerable<Account>> GetByUserIdAsync(Guid userId);

    /// <summary>
    /// Fetches all accounts for a user alongside their transactions up to <paramref name="upToDate"/>.
    /// Uses two queries rather than a JOIN to avoid row explosion on accounts with many transactions.
    /// </summary>
    Task<(IEnumerable<Account> Accounts, IEnumerable<Transaction> Transactions)>
        GetByUserIdWithTransactionsUpToDateAsync(Guid userId, DateTimeOffset upToDate);

    Task<Account> CreateAsync(Account account);
    Task<Account> UpdateAsync(Account account);
    Task<bool> DeleteAsync(Guid accountId);
    Task<bool> UserOwnsAccountAsync(Guid userId, Guid accountId);
}
