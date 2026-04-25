using HexFund.Application.DTOs;
using HexFund.Core.Entities;

namespace HexFund.Application.Interfaces;

public interface ICashFlowService
{
    Task<EnhancedCashFlowProjection?> GetEnhancedCashFlowProjectionAsync(
        Guid accountId, Guid userId, DateTimeOffset startDate, DateTimeOffset endDate,
        bool includeProjectedRecurring = true);

    Task<EnhancedMonthlyOverview?> GetEnhancedMonthlyOverviewAsync(
        Guid accountId, Guid userId, int year, int month);

    Task<List<TransactionOccurrence>?> GetTransactionsForDateAsync(
        Guid accountId, Guid userId, DateTimeOffset date);

    Task<Dictionary<DateTime, List<TransactionOccurrence>>?> GetTransactionsForMonthAsync(
        Guid accountId, Guid userId, int year, int month);

    Task<List<DailyBalanceSnapshot>?> GetRollingBalanceSnapshotsAsync(
        Guid accountId, Guid userId, DateTimeOffset startDate, DateTimeOffset endDate);

    Task<decimal?> GetBalanceAtDateAsync(Guid accountId, Guid userId, DateTimeOffset asOfDate);

    Task<decimal?> GetDailyBalanceAsync(Guid accountId, Guid userId, DateTimeOffset date);

    Task<List<TransactionWithBalanceDto>?> GetTransactionsWithRunningBalancesAsync(
        Guid accountId, Guid userId, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null);

    decimal CalculateBalanceFromTransactions(
        IEnumerable<Transaction> transactions, decimal initialBalance, DateTimeOffset asOfDate);
}
