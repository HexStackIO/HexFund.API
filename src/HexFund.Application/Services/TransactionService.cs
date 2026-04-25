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

    public TransactionService(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
    }

    public async Task<IEnumerable<TransactionDto>> GetAccountTransactionsAsync(
        Guid accountId, Guid userId, bool includeHistory = false)
    {
        if (!await _accountRepository.UserOwnsAccountAsync(userId, accountId))
            return Enumerable.Empty<TransactionDto>();

        var transactions = await _transactionRepository.GetByAccountIdAsync(accountId);

        if (!includeHistory)
        {
            var now = DateTimeOffset.UtcNow;
            transactions = transactions.Where(t =>
                t.PredecessorTransactionId == null || !t.EndDate.HasValue || t.EndDate >= now);
        }

        return transactions.Select(MapToDto);
    }

    public async Task<TransactionDto?> GetTransactionByIdAsync(Guid transactionId, Guid userId)
    {
        var transaction = await _transactionRepository.GetByIdAsync(transactionId);
        if (transaction == null)
            return null;

        if (!await _accountRepository.UserOwnsAccountAsync(userId, transaction.AccountId))
            return null;

        return MapToDto(transaction);
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
            Frequency = (FrequencyType)request.Frequency,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = true,
            Color = request.Color
        };

        await _transactionRepository.CreateAsync(transaction);
        return MapToDto(transaction);
    }

    public async Task<TransactionDto?> UpdateTransactionAsync(
        Guid transactionId, UpdateTransactionRequest request, Guid userId)
    {
        var transaction = await _transactionRepository.GetByIdAsync(transactionId);
        if (transaction == null)
            return null;

        if (!await _accountRepository.UserOwnsAccountAsync(userId, transaction.AccountId))
            return null;

        transaction.Description = request.Description;
        transaction.Amount = request.Amount;
        transaction.Category = request.Category;
        transaction.Frequency = (FrequencyType)request.Frequency;
        transaction.StartDate = request.StartDate;
        transaction.EndDate = request.EndDate;
        transaction.IsActive = request.IsActive;
        transaction.Color = request.Color;

        await _transactionRepository.UpdateAsync(transaction);
        return MapToDto(transaction);
    }

    public async Task<bool> DeleteTransactionAsync(Guid transactionId, Guid userId)
    {
        var transaction = await _transactionRepository.GetByIdAsync(transactionId);
        if (transaction == null)
            return false;

        if (!await _accountRepository.UserOwnsAccountAsync(userId, transaction.AccountId))
            return false;

        // If this is an amend successor, restore the predecessor to open-ended.
        if (transaction.PredecessorTransactionId.HasValue)
        {
            var predecessor = await _transactionRepository.GetByIdAsync(
                transaction.PredecessorTransactionId.Value);
            if (predecessor != null)
            {
                predecessor.EndDate = null;
                await _transactionRepository.UpdateAsync(predecessor);
            }
        }

        return await _transactionRepository.DeleteAsync(transactionId);
    }

    /// <summary>
    /// Amend a recurring transaction with new values starting on request.EffectiveDate.
    ///
    /// Steps:
    ///   1. Validate ownership and that EffectiveDate is strictly after the original StartDate.
    ///   2. End-date the original transaction to EffectiveDate − 1 day.
    ///   3. Create a successor row inheriting all fields, with the caller's overrides applied,
    ///      StartDate = EffectiveDate, and PredecessorTransactionId pointing to the original.
    /// </summary>
    public async Task<TransactionDto?> AmendTransactionAsync(
        Guid transactionId, AmendTransactionRequest request, Guid userId)
    {
        var original = await _transactionRepository.GetByIdAsync(transactionId);
        if (original == null)
            return null;

        if (!await _accountRepository.UserOwnsAccountAsync(userId, original.AccountId))
            return null;

        if (request.EffectiveDate <= original.StartDate)
            return null;

        original.EndDate = request.EffectiveDate.AddDays(-1);
        await _transactionRepository.UpdateAsync(original);

        var successor = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            AccountId = original.AccountId,
            Description = request.Description ?? original.Description,
            Amount = request.Amount,
            Category = request.Category ?? original.Category,
            Frequency = original.Frequency,
            StartDate = request.EffectiveDate,
            EndDate = null,
            IsActive = true,
            Color = request.Color ?? original.Color,
            PredecessorTransactionId = original.TransactionId,
        };

        await _transactionRepository.CreateAsync(successor);
        return MapToDto(successor);
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
        PredecessorTransactionId = t.PredecessorTransactionId,
    };
}
