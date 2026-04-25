using HexFund.Core.Entities;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HexFund.Infrastructure.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly HexFundDbContext _context;

    public CategoryRepository(HexFundDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<UserCategory>> GetByUserIdAsync(Guid userId) =>
        await _context.UserCategories
            .Where(c => c.UserId == userId)
            .AsNoTracking()
            .ToListAsync();

    public async Task<UserCategory?> GetByIdAsync(Guid categoryId) =>
        await _context.UserCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId);

    public async Task CreateAsync(UserCategory category)
    {
        _context.UserCategories.Add(category);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid categoryId)
    {
        var category = await _context.UserCategories
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId);

        if (category != null)
        {
            _context.UserCategories.Remove(category);
            await _context.SaveChangesAsync();
        }
    }
}