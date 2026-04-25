using HexFund.Core.Entities;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HexFund.Infrastructure.Repositories;

/// <summary>
/// Repository for Transaction entity operations
/// </summary>
public class TransactionRepository : ITransactionRepository
{
    private readonly HexFundDbContext _context;

    public TransactionRepository(HexFundDbContext context)
    {
        _context = context;
    }

    public async Task<Transaction?> GetByIdAsync(Guid transactionId)
    {
        return await _context.Transactions
            .Include(t => t.Account)
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
    }

    public async Task<IEnumerable<Transaction>> GetByAccountIdAsync(Guid accountId)
    {
        return await _context.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.StartDate)
            .ToListAsync();
    }

    public async Task<Transaction> CreateAsync(Transaction transaction)
    {
        transaction.CreatedAt = DateTime.UtcNow;
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();
        return transaction;
    }

    public async Task<Transaction> UpdateAsync(Transaction transaction)
    {
        transaction.ModifiedAt = DateTime.UtcNow;
        _context.Transactions.Update(transaction);
        await _context.SaveChangesAsync();
        return transaction;
    }

    public async Task<bool> DeleteAsync(Guid transactionId)
    {
        var transaction = await _context.Transactions.FindAsync(transactionId);
        if (transaction == null)
            return false;

        _context.Transactions.Remove(transaction);
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Get transactions for an account that overlap with a date range.
    /// This properly handles recurring transactions by checking if:
    /// 1. The transaction starts on or before the end of the range
    /// 2. The transaction has no end date (ongoing) OR ends on or after the start of the range
    /// 
    /// Example: A monthly transaction starting 2/6/26 with no end date will match:
    /// - February 2026 query (starts 2/6, no end)
    /// - March 2026 query (started before 3/31, no end)
    /// - All future months (no end date means it continues)
    /// </summary>
    public async Task<IEnumerable<Transaction>> GetByAccountAndDateRangeAsync(
        Guid accountId,
        DateTimeOffset startDate,
        DateTimeOffset endDate)
    {
        return await _context.Transactions
            .Where(t => t.AccountId == accountId
                && t.IsActive                                         // Inactive transactions excluded from calendar
                && t.StartDate <= endDate                             // Transaction starts on or before end of range
                && (!t.EndDate.HasValue || t.EndDate >= startDate))  // No end date OR ends on or after start of range
            .OrderBy(t => t.StartDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Transaction>> GetByAccountUpToDateAsync(
        Guid accountId,
        DateTimeOffset upToDate)
    {
        return await _context.Transactions
            .Where(t => t.AccountId == accountId
                && t.IsActive                  // Inactive transactions excluded from balance calculations
                && t.StartDate <= upToDate)
            .OrderBy(t => t.StartDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
    }
}