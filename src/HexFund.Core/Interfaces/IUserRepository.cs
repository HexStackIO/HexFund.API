using HexFund.Core.Entities;

namespace HexFund.Core.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid userId);
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByEntraIdAsync(string entraObjectId);
    Task<User> CreateAsync(User user);
    Task<User> UpdateAsync(User user);
    Task<bool> DeleteAsync(Guid userId);
    Task<bool> ExistsAsync(string email);
}
