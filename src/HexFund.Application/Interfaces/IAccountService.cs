using HexFund.Application.DTOs;

namespace HexFund.Application.Interfaces;

public interface IAccountService
{
    Task<IEnumerable<AccountDto>> GetUserAccountsAsync(Guid userId);
    Task<AccountDto?> GetAccountByIdAsync(Guid accountId, Guid userId);
    Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, Guid userId);
    Task<AccountDto?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, Guid userId);
    Task<bool> DeleteAccountAsync(Guid accountId, Guid userId);
}
