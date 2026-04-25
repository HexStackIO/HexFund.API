using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.Core.Entities;
using HexFund.Core.Interfaces;

namespace HexFund.Application.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ICashFlowService _cashFlowService;

    public AccountService(IAccountRepository accountRepository, ICashFlowService cashFlowService)
    {
        _accountRepository = accountRepository;
        _cashFlowService = cashFlowService;
    }

    public async Task<IEnumerable<AccountDto>> GetUserAccountsAsync(Guid userId)
    {
        var asOfDate = DateTimeOffset.UtcNow;
        var (accounts, transactions) =
            await _accountRepository.GetByUserIdWithTransactionsUpToDateAsync(userId, asOfDate);

        var txByAccount = transactions
            .GroupBy(t => t.AccountId)
            .ToDictionary(g => g.Key, g => (IEnumerable<Transaction>)g);

        return accounts.Select(account =>
        {
            txByAccount.TryGetValue(account.AccountId, out var accountTx);
            var balance = _cashFlowService.CalculateBalanceFromTransactions(
                accountTx ?? Enumerable.Empty<Transaction>(),
                account.InitialBalance,
                asOfDate);
            return MapToDto(account, balance);
        }).ToList();
    }

    public async Task<AccountDto?> GetAccountByIdAsync(Guid accountId, Guid userId)
    {
        if (!await _accountRepository.UserOwnsAccountAsync(userId, accountId))
            return null;

        var account = await _accountRepository.GetByIdAsync(accountId);
        if (account == null)
            return null;

        return await MapToDtoWithCurrentBalanceAsync(account, userId);
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
        return MapToDto(account, request.InitialBalance);
    }

    public async Task<AccountDto?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, Guid userId)
    {
        if (!await _accountRepository.UserOwnsAccountAsync(userId, accountId))
            return null;

        var account = await _accountRepository.GetByIdAsync(accountId);
        if (account == null)
            return null;

        account.AccountName = request.AccountName;
        account.IsActive = request.IsActive;

        await _accountRepository.UpdateAsync(account);
        return await MapToDtoWithCurrentBalanceAsync(account, userId);
    }

    public async Task<bool> DeleteAccountAsync(Guid accountId, Guid userId)
    {
        if (!await _accountRepository.UserOwnsAccountAsync(userId, accountId))
            return false;

        return await _accountRepository.DeleteAsync(accountId);
    }

    private async Task<AccountDto> MapToDtoWithCurrentBalanceAsync(Account account, Guid userId)
    {
        var currentBalance = await _cashFlowService.GetBalanceAtDateAsync(
            account.AccountId, userId, DateTimeOffset.UtcNow);

        return MapToDto(account, currentBalance ?? account.InitialBalance);
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
