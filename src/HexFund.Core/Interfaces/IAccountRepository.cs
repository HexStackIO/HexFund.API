using HexFund.Core.Entities;

namespace HexFund.Core.Interfaces;

public interface IAccountRepository
{
    /// <summary>Returns the account only if it belongs to <paramref name="userId"/>.</summary>
    Task<Account?> GetByIdForUserAsync(Guid userId, Guid accountId);

    /// <summary>
    /// Fetches all accounts for a user alongside their active transactions up to <paramref name="upToDate"/>.
    /// Uses two queries rather than a JOIN so accounts with many transactions do not multiply rows.
    /// </summary>
    Task<(IReadOnlyList<Account> Accounts, IReadOnlyList<Transaction> Transactions)>
        GetByUserIdWithTransactionsUpToDateAsync(Guid userId, DateTimeOffset upToDate);

    Task<Account> CreateAsync(Account account);
    Task<Account> UpdateAsync(Account account);

    /// <summary>Deletes the account (and its transactions) if owned by the user. Returns false if not found.</summary>
    Task<bool> DeleteForUserAsync(Guid userId, Guid accountId);

    Task<bool> UserOwnsAccountAsync(Guid userId, Guid accountId);
}
