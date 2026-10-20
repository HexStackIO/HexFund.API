using HexFund.Core.Entities;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HexFund.Infrastructure.Repositories;

public class AccountRepository : IAccountRepository
{
    private readonly HexFundDbContext _context;

    public AccountRepository(HexFundDbContext context)
    {
        _context = context;
    }

    public async Task<Account?> GetByIdForUserAsync(Guid userId, Guid accountId) =>
        await _context.Accounts
            .FirstOrDefaultAsync(a => a.AccountId == accountId && a.UserId == userId);

    public async Task<(IReadOnlyList<Account> Accounts, IReadOnlyList<Transaction> Transactions)>
        GetByUserIdWithTransactionsUpToDateAsync(Guid userId, DateTimeOffset upToDate)
    {
        var accounts = await _context.Accounts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        if (accounts.Count == 0)
            return (accounts, Array.Empty<Transaction>());

        var accountIds = accounts.Select(a => a.AccountId).ToList();
        var transactions = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId)
                     && t.IsActive
                     && t.StartDate <= upToDate)
            .ToListAsync();

        return (accounts, transactions);
    }

    public async Task<Account> CreateAsync(Account account)
    {
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    public async Task<Account> UpdateAsync(Account account)
    {
        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();
        return account;
    }

    // Single DELETE statement: lets PostgreSQL resolve the Transactions self-reference
    // (ON DELETE SET NULL) and the account cascade in the right order.
    public async Task<bool> DeleteForUserAsync(Guid userId, Guid accountId) =>
        await _context.Accounts
            .Where(a => a.AccountId == accountId && a.UserId == userId)
            .ExecuteDeleteAsync() > 0;

    public Task<bool> UserOwnsAccountAsync(Guid userId, Guid accountId) =>
        _context.Accounts.AnyAsync(a => a.AccountId == accountId && a.UserId == userId);
}
