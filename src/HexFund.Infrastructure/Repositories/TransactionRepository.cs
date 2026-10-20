using HexFund.Core.Entities;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HexFund.Infrastructure.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly HexFundDbContext _context;

    public TransactionRepository(HexFundDbContext context)
    {
        _context = context;
    }

    public async Task<Transaction?> GetByIdForUserAsync(Guid transactionId, Guid userId) =>
        await _context.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.Account.UserId == userId);

    public async Task<IReadOnlyList<Transaction>> GetByAccountIdAsync(Guid accountId) =>
        await _context.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.StartDate)
            .ToListAsync();

    public async Task<Transaction> CreateAsync(Transaction transaction)
    {
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();
        return transaction;
    }

    public async Task<Transaction> UpdateAsync(Transaction transaction)
    {
        _context.Transactions.Update(transaction);
        await _context.SaveChangesAsync();
        return transaction;
    }

    public async Task<bool> DeleteAsync(Guid transactionId)
    {
        var transaction = await _context.Transactions
            .AsTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
        if (transaction == null)
            return false;

        // Deleting an amendment successor restores its predecessor (the successor inherited
        // the original end date, so handing it back is exact), in the same save.
        if (transaction.PredecessorTransactionId is { } predecessorId)
        {
            var predecessor = await _context.Transactions
                .AsTracking()
                .FirstOrDefaultAsync(t => t.TransactionId == predecessorId);
            if (predecessor != null)
                predecessor.EndDate = transaction.EndDate;
        }

        _context.Transactions.Remove(transaction);
        await _context.SaveChangesAsync();
        return true;
    }

    public Task<bool> HasSuccessorAsync(Guid transactionId) =>
        _context.Transactions.AnyAsync(t => t.PredecessorTransactionId == transactionId);

    public async Task AmendAsync(Transaction original, Transaction successor)
    {
        _context.Transactions.Update(original);
        _context.Transactions.Add(successor);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Transactions that overlap a date range: they start on or before the end of the
    /// range and are either open-ended or end on/after the start of the range.
    /// Inactive transactions are excluded.
    /// </summary>
    public async Task<IReadOnlyList<Transaction>> GetByAccountAndDateRangeAsync(
        Guid accountId, DateTimeOffset startDate, DateTimeOffset endDate) =>
        await _context.Transactions
            .Where(t => t.AccountId == accountId
                && t.IsActive
                && t.StartDate <= endDate
                && (!t.EndDate.HasValue || t.EndDate >= startDate))
            .ToListAsync();

    public async Task<IReadOnlyList<Transaction>> GetByAccountUpToDateAsync(
        Guid accountId, DateTimeOffset upToDate) =>
        await _context.Transactions
            .Where(t => t.AccountId == accountId
                && t.IsActive
                && t.StartDate <= upToDate)
            .ToListAsync();
}
