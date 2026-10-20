using HexFund.Core.Entities;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HexFund.Infrastructure.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly HexFundDbContext _context;

    public CategoryRepository(HexFundDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<UserCategory>> GetByUserIdAsync(Guid userId) =>
        await _context.UserCategories
            .Where(c => c.UserId == userId)
            .ToListAsync();

    public Task<bool> ExistsAsync(Guid userId, string name)
    {
        var lowered = name.ToLower();
        return _context.UserCategories.AnyAsync(c => c.UserId == userId && c.Name.ToLower() == lowered);
    }

    public async Task<bool> TryCreateAsync(UserCategory category)
    {
        _context.UserCategories.Add(category);
        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _context.Entry(category).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> DeleteForUserAsync(Guid userId, Guid categoryId) =>
        await _context.UserCategories
            .Where(c => c.CategoryId == categoryId && c.UserId == userId)
            .ExecuteDeleteAsync() > 0;
}
