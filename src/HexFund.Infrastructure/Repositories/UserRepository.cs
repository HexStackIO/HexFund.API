using HexFund.Core.Entities;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HexFund.Infrastructure.Repositories;

/// <summary>
/// Repository for User entity operations
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly HexFundDbContext _context;

    public UserRepository(HexFundDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid userId)
    {
        return await _context.Users
            .Include(u => u.Accounts)
            .FirstOrDefaultAsync(u => u.UserId == userId);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
    }

    public async Task<User?> GetByEntraIdAsync(string entraObjectId)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId);
    }

    public async Task<User> CreateAsync(User user)
    {
        user.CreatedAt = DateTimeOffset.UtcNow;
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<User> UpdateAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<bool> DeleteAsync(Guid userId)
    {
        // Use ExecuteDeleteAsync to emit a single DELETE statement rather than
        // loading the entity into the change tracker and letting EF generate
        // individual deletes. This is critical because the Transactions table has
        // a self-referential FK (PredecessorTransactionId → TransactionId ON DELETE
        // SET NULL). If EF loads and deletes tracked entities individually it may
        // issue DELETEs in an order that conflicts with that self-reference before
        // the database cascade can resolve it.
        //
        // A single DELETE FROM Users WHERE UserId = X lets PostgreSQL execute the
        // full cascade in the correct dependency order internally:
        //   1. SET NULL on Transactions.PredecessorTransactionId (self-ref)
        //   2. DELETE Transactions (via Account → CASCADE)
        //   3. DELETE Accounts (via User → CASCADE)
        //   4. DELETE UserCategories (via User → CASCADE)
        //   5. DELETE User row
        //
        // ExecuteDeleteAsync bypasses the change tracker entirely and returns the
        // number of rows affected at the root (Users) table.
        var rowsDeleted = await _context.Users
            .Where(u => u.UserId == userId)
            .ExecuteDeleteAsync();

        return rowsDeleted > 0;
    }

    public async Task<bool> ExistsAsync(string email)
    {
        return await _context.Users
            .AnyAsync(u => u.Email.ToLower() == email.ToLower());
    }
}
