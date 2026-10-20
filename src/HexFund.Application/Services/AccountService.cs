using HexFund.Application.Calculation;
using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.Core.Entities;
using HexFund.Core.Interfaces;

namespace HexFund.Application.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly ICacheService _cache;

    public AccountService(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        ICacheService cache)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _cache = cache;
    }

    public async Task<IReadOnlyList<AccountDto>> GetUserAccountsAsync(Guid userId)
    {
        var asOf = DateTimeOffset.UtcNow;
        var (accounts, transactions) =
            await _accountRepository.GetByUserIdWithTransactionsUpToDateAsync(userId, asOf);

        var byAccount = transactions.ToLookup(t => t.AccountId);

        return accounts
            .Select(a => MapToDto(a, BalanceCalculator.BalanceAt(a.InitialBalance, byAccount[a.AccountId], asOf)))
            .ToList();
    }

    public async Task<AccountDto?> GetAccountByIdAsync(Guid accountId, Guid userId)
    {
        var account = await _accountRepository.GetByIdForUserAsync(userId, accountId);
        return account == null ? null : await MapWithCurrentBalanceAsync(account);
    }

    public async Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, Guid userId)
    {
        var account = new Account
        {
            AccountId = Guid.NewGuid(),
            UserId = userId,
            AccountName = request.AccountName,
            InitialBalance = request.InitialBalance,
            Currency = request.Currency,
            IsActive = true
        };

        await _accountRepository.CreateAsync(account);
        return MapToDto(account, account.InitialBalance);
    }

    public async Task<AccountDto?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, Guid userId)
    {
        var account = await _accountRepository.GetByIdForUserAsync(userId, accountId);
        if (account == null)
            return null;

        account.AccountName = request.AccountName;
        account.IsActive = request.IsActive;
        await _accountRepository.UpdateAsync(account);

        return await MapWithCurrentBalanceAsync(account);
    }

    public async Task<bool> DeleteAccountAsync(Guid accountId, Guid userId)
    {
        var deleted = await _accountRepository.DeleteForUserAsync(userId, accountId);
        if (deleted)
            _cache.InvalidateAccount(accountId);
        return deleted;
    }

    private async Task<AccountDto> MapWithCurrentBalanceAsync(Account account)
    {
        var asOf = DateTimeOffset.UtcNow;
        var transactions = await _transactionRepository.GetByAccountUpToDateAsync(account.AccountId, asOf);
        return MapToDto(account, BalanceCalculator.BalanceAt(account.InitialBalance, transactions, asOf));
    }

    private static AccountDto MapToDto(Account account, decimal currentBalance) => new()
    {
        AccountId = account.AccountId,
        AccountName = account.AccountName,
        InitialBalance = account.InitialBalance,
        CurrentBalance = currentBalance,
        Currency = account.Currency,
        IsActive = account.IsActive,
        CreatedAt = account.CreatedAt
    };
}
