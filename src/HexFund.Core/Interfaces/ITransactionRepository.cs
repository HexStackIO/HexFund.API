using HexFund.Core.Entities;

namespace HexFund.Core.Interfaces;

public interface ITransactionRepository
{
    /// <summary>Returns the transaction only if its account belongs to <paramref name="userId"/>.</summary>
    Task<Transaction?> GetByIdForUserAsync(Guid transactionId, Guid userId);

    Task<IReadOnlyList<Transaction>> GetByAccountIdAsync(Guid accountId);

    Task<Transaction> CreateAsync(Transaction transaction);
    Task<Transaction> UpdateAsync(Transaction transaction);

    /// <summary>
    /// Deletes the transaction. If it is an amendment successor, its predecessor is
    /// given back its end date in the same save, so the operation is atomic.
    /// </summary>
    Task<bool> DeleteAsync(Guid transactionId);

    /// <summary>True when another transaction names <paramref name="transactionId"/> as its predecessor.</summary>
    Task<bool> HasSuccessorAsync(Guid transactionId);

    /// <summary>Persists the end-dated original and its new successor in a single atomic save.</summary>
    Task AmendAsync(Transaction original, Transaction successor);

    /// <summary>Active transactions overlapping [startDate, endDate].</summary>
    Task<IReadOnlyList<Transaction>> GetByAccountAndDateRangeAsync(
        Guid accountId, DateTimeOffset startDate, DateTimeOffset endDate);

    /// <summary>Active transactions that start on or before <paramref name="upToDate"/>.</summary>
    Task<IReadOnlyList<Transaction>> GetByAccountUpToDateAsync(
        Guid accountId, DateTimeOffset upToDate);
}
