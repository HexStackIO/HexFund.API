using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.Core.Entities;
using HexFund.Core.Enums;
using HexFund.Core.Interfaces;

namespace HexFund.Application.Services;

public class TransactionService : ITransactionService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ICacheService _cache;

    public TransactionService(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        ICacheService cache)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _cache = cache;
    }

    public async Task<IReadOnlyList<TransactionDto>?> GetAccountTransactionsAsync(
        Guid accountId, Guid userId, bool includeHistory = false)
    {
        if (!await _accountRepository.UserOwnsAccountAsync(userId, accountId))
            return null;

        var transactions = await _transactionRepository.GetByAccountIdAsync(accountId);

        if (!includeHistory)
        {
            // A transaction that has been amended is superseded by its successor.
            var superseded = transactions
                .Where(t => t.PredecessorTransactionId.HasValue)
                .Select(t => t.PredecessorTransactionId!.Value)
                .ToHashSet();

            transactions = transactions.Where(t => !superseded.Contains(t.TransactionId)).ToList();
        }

        return transactions.Select(MapToDto).ToList();
    }

    public async Task<TransactionDto?> GetTransactionByIdAsync(Guid transactionId, Guid userId)
    {
        var transaction = await _transactionRepository.GetByIdForUserAsync(transactionId, userId);
        return transaction == null ? null : MapToDto(transaction);
    }

    public async Task<TransactionDto?> CreateTransactionAsync(
        Guid accountId, CreateTransactionRequest request, Guid userId)
    {
        if (!await _accountRepository.UserOwnsAccountAsync(userId, accountId))
            return null;

        var transaction = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            AccountId = accountId,
            Description = request.Description,
            Amount = request.Amount,
            Category = request.Category,
            Frequency = request.Frequency,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = true,
            Color = request.Color
        };

        await _transactionRepository.CreateAsync(transaction);
        _cache.InvalidateAccount(accountId);
        return MapToDto(transaction);
    }

    public async Task<TransactionDto?> UpdateTransactionAsync(
        Guid accountId, Guid transactionId, UpdateTransactionRequest request, Guid userId)
    {
        var transaction = await GetOwnedAsync(accountId, transactionId, userId);
        if (transaction == null)
            return null;

        transaction.Description = request.Description;
        transaction.Amount = request.Amount;
        transaction.Category = request.Category;
        transaction.Frequency = request.Frequency;
        transaction.StartDate = request.StartDate;
        transaction.EndDate = request.EndDate;
        transaction.IsActive = request.IsActive;
        transaction.Color = request.Color;

        await _transactionRepository.UpdateAsync(transaction);
        _cache.InvalidateAccount(accountId);
        return MapToDto(transaction);
    }

    public async Task<bool> DeleteTransactionAsync(Guid accountId, Guid transactionId, Guid userId)
    {
        if (await GetOwnedAsync(accountId, transactionId, userId) == null)
            return false;

        // The repository restores the predecessor atomically when deleting an amendment successor.
        var deleted = await _transactionRepository.DeleteAsync(transactionId);
        if (deleted)
            _cache.InvalidateAccount(accountId);
        return deleted;
    }

    /// <summary>
    /// Applies new values to a recurring transaction from request.EffectiveDate forward:
    /// the original is end-dated to EffectiveDate - 1 day and a successor row inheriting
    /// its fields (with the request's overrides) starts on EffectiveDate. Both rows are
    /// saved atomically.
    /// </summary>
    public async Task<TransactionDto?> AmendTransactionAsync(
        Guid accountId, Guid transactionId, AmendTransactionRequest request, Guid userId)
    {
        var original = await GetOwnedAsync(accountId, transactionId, userId);
        if (original == null)
            return null;

        if (original.Frequency == FrequencyType.Once)
            throw new ArgumentException("Only recurring transactions can be amended.");

        if (request.EffectiveDate <= original.StartDate)
            throw new ArgumentException("Effective date must be after the transaction's start date.");

        if (original.EndDate.HasValue && request.EffectiveDate > original.EndDate.Value)
            throw new ArgumentException("Effective date must be before the transaction's end date.");

        if (await _transactionRepository.HasSuccessorAsync(transactionId))
            throw new ArgumentException("This transaction has already been amended; amend its latest version instead.");

        var successor = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            AccountId = original.AccountId,
            Description = request.Description ?? original.Description,
            Amount = request.Amount,
            Category = request.Category ?? original.Category,
            Frequency = original.Frequency,
            StartDate = request.EffectiveDate,
            EndDate = original.EndDate,
            IsActive = true,
            Color = request.Color ?? original.Color,
            PredecessorTransactionId = original.TransactionId
        };

        original.EndDate = request.EffectiveDate.AddDays(-1);

        await _transactionRepository.AmendAsync(original, successor);
        _cache.InvalidateAccount(accountId);
        return MapToDto(successor);
    }

    /// <summary>Loads the transaction if it exists, belongs to the user, and belongs to the route's account.</summary>
    private async Task<Transaction?> GetOwnedAsync(Guid accountId, Guid transactionId, Guid userId)
    {
        var transaction = await _transactionRepository.GetByIdForUserAsync(transactionId, userId);
        return transaction?.AccountId == accountId ? transaction : null;
    }

    private static TransactionDto MapToDto(Transaction t) => new()
    {
        TransactionId = t.TransactionId,
        Description = t.Description,
        Amount = t.Amount,
        Category = t.Category,
        Frequency = (int)t.Frequency,
        StartDate = t.StartDate,
        EndDate = t.EndDate,
        IsActive = t.IsActive,
        Color = t.Color,
        PredecessorTransactionId = t.PredecessorTransactionId
    };
}
