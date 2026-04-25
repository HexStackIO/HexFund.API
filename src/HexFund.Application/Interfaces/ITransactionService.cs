using HexFund.Application.DTOs;

namespace HexFund.Application.Interfaces;

public interface ITransactionService
{
    Task<IEnumerable<TransactionDto>> GetAccountTransactionsAsync(Guid accountId, Guid userId, bool includeHistory = false);
    Task<TransactionDto?> GetTransactionByIdAsync(Guid transactionId, Guid userId);
    Task<TransactionDto?> CreateTransactionAsync(Guid accountId, CreateTransactionRequest request, Guid userId);
    Task<TransactionDto?> UpdateTransactionAsync(Guid transactionId, UpdateTransactionRequest request, Guid userId);
    Task<bool> DeleteTransactionAsync(Guid transactionId, Guid userId);
    Task<TransactionDto?> AmendTransactionAsync(Guid transactionId, AmendTransactionRequest request, Guid userId);
}
