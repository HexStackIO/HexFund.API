using HexFund.Core.Entities;

namespace HexFund.Core.Interfaces;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid transactionId);
    Task<IEnumerable<Transaction>> GetByAccountIdAsync(Guid accountId);
    Task<Transaction> CreateAsync(Transaction transaction);
    Task<Transaction> UpdateAsync(Transaction transaction);
    Task<bool> DeleteAsync(Guid transactionId);

    Task<IEnumerable<Transaction>> GetByAccountAndDateRangeAsync(
    Guid accountId,
    DateTimeOffset startDate,
    DateTimeOffset endDate);

    Task<IEnumerable<Transaction>> GetByAccountUpToDateAsync(
        Guid accountId,
        DateTimeOffset upToDate);
}
