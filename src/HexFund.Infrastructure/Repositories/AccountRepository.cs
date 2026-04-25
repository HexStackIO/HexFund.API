using HexFund.Core.Entities;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HexFund.Infrastructure.Repositories;

/// <summary>
/// Repository for Account entity operations
/// </summary>
public class AccountRepository : IAccountRepository
{
    private readonly HexFundDbContext _context;

    public AccountRepository(HexFundDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Fetch a single account WITHOUT eagerly loading its Transactions.
    /// Callers that need transactions should query ITransactionRepository directly.
    /// </summary>
    public async Task<Account?> GetByIdAsync(Guid accountId)
    {
        return await _context.Accounts
            .FirstOrDefaultAsync(a => a.AccountId == accountId);
    }

    public async Task<IEnumerable<Account>> GetByUserIdAsync(Guid userId)
    {
        return await _context.Accounts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Two-query load returning accounts and their active transactions up to a given date.
    /// Returns an explicit tuple rather than relying on EF navigation fixup to avoid
    /// stale/unfiltered data being merged into navigation collections by EF.
    /// </summary>
    public async Task<(IEnumerable<Account> Accounts, IEnumerable<Transaction> Transactions)>
        GetByUserIdWithTransactionsUpToDateAsync(Guid userId, DateTimeOffset upToDate)
    {
        var accounts = await _context.Accounts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        if (!accounts.Any())
            return (accounts, Enumerable.Empty<Transaction>());

        var accountIds = accounts.Select(a => a.AccountId).ToList();
        var transactions = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId)
                     && t.IsActive
                     && t.StartDate <= upToDate)
            .OrderBy(t => t.StartDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();

        return (accounts, transactions);
    }

    public async Task<Account> CreateAsync(Account account)
    {
        account.CreatedAt = DateTimeOffset.UtcNow;
        account.CurrentBalance = account.InitialBalance;
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    public async Task<Account> UpdateAsync(Account account)
    {
        account.ModifiedAt = DateTimeOffset.UtcNow;
        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();
        return account;
    }

    public async Task<bool> DeleteAsync(Guid accountId)
    {
        var account = await _context.Accounts.FindAsync(accountId);
        if (account == null)
            return false;

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UserOwnsAccountAsync(Guid userId, Guid accountId)
    {
        return await _context.Accounts
            .AnyAsync(a => a.AccountId == accountId && a.UserId == userId);
    }
}
