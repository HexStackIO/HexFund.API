using HexFund.Core.Entities;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HexFund.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly HexFundDbContext _context;

    public UserRepository(HexFundDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid userId) =>
        await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

    public async Task<User?> GetByEntraIdAsync(string entraObjectId) =>
        await _context.Users.FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId);

    public async Task<User> CreateAsync(User user)
    {
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

    // A single DELETE lets PostgreSQL run the whole cascade in dependency order
    // (Transactions self-reference SET NULL -> Transactions -> Accounts / UserCategories -> User).
    // Loading entities into the change tracker could issue conflicting deletes.
    public async Task<bool> DeleteAsync(Guid userId) =>
        await _context.Users.Where(u => u.UserId == userId).ExecuteDeleteAsync() > 0;
}
