using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.Core.Entities;
using HexFund.Core.Enums;
using HexFund.Core.Interfaces;

namespace HexFund.Application.Services;

public class CashFlowService : ICashFlowService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly TransactionRecurrenceService _recurrenceService;

    public CashFlowService(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        TransactionRecurrenceService recurrenceService)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _recurrenceService = recurrenceService;
    }

    public async Task<EnhancedCashFlowProjection?> GetEnhancedCashFlowProjectionAsync(
        Guid accountId, Guid userId, DateTimeOffset startDate, DateTimeOffset endDate,
        bool includeProjectedRecurring = true)
    {
        var account = await _accountRepository.GetByIdAsync(accountId);
        if (account == null || account.UserId != userId)
            return null;

        var transactionsList = await BuildTransactionListForRangeAsync(
            accountId, startDate, endDate, includeProjectedRecurring);

        var startingBalance = await CalculateBalanceAtDateInternalAsync(
            accountId,
            startDate.AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59),
            account.InitialBalance);

        return BuildProjection(account, startDate, endDate, startingBalance, transactionsList);
    }

    public async Task<EnhancedMonthlyOverview?> GetEnhancedMonthlyOverviewAsync(
        Guid accountId, Guid userId, int year, int month)
    {
        var startDate = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var endDate = startDate.AddMonths(1).AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);

        var projection = await GetEnhancedCashFlowProjectionAsync(
            accountId, userId, startDate, endDate, includeProjectedRecurring: true);

        if (projection == null)
            return null;

        var avgDailyBalance = projection.DailySnapshots.Any()
            ? projection.DailySnapshots.Average(s => s.EndOfDayBalance) : 0;
        var highestBalance = projection.DailySnapshots.Any()
            ? projection.DailySnapshots.Max(s => s.EndOfDayBalance) : projection.StartingBalance;
        var lowestBalance = projection.DailySnapshots.Any()
            ? projection.DailySnapshots.Min(s => s.LowestBalance) : projection.StartingBalance;
        var daysWithNegativeBalance = projection.DailySnapshots.Count(s => s.HasNegativeBalance);

        return new EnhancedMonthlyOverview
        {
            Year = year,
            Month = month,
            AccountId = accountId,
            StartingBalance = projection.StartingBalance,
            EndingBalance = projection.EndingBalance,
            NetChange = projection.NetChange,
            TotalIncome = projection.TotalIncome,
            TotalExpenses = projection.TotalExpenses,
            AverageDailyBalance = avgDailyBalance,
            HighestBalance = highestBalance,
            LowestBalance = lowestBalance,
            DaysWithNegativeBalance = daysWithNegativeBalance,
            DailyBreakdown = projection.DailySnapshots,
            CategoryBreakdowns = CalculateCategoryBreakdowns(projection.Transactions)
        };
    }

    public async Task<List<TransactionOccurrence>?> GetTransactionsForDateAsync(
        Guid accountId, Guid userId, DateTimeOffset date)
    {
        var account = await _accountRepository.GetByIdAsync(accountId);
        if (account == null || account.UserId != userId)
            return null;

        var startOfDay = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
        var endOfDay = startOfDay.AddDays(1).AddSeconds(-1);

        var transactions = await BuildTransactionListForRangeAsync(
            accountId, startOfDay, endOfDay, includeProjectedRecurring: true);

        return transactions
            .Select(t => new TransactionOccurrence
            {
                TransactionId = t.TransactionId,
                Description = t.Description,
                Amount = t.Amount,
                Category = t.Category ?? string.Empty,
                OccurrenceDate = t.StartDate.DateTime,
                Frequency = FrequencyType.Once,
                Color = t.Color
            })
            .OrderBy(t => t.OccurrenceDate)
            .ToList();
    }

    public async Task<Dictionary<DateTime, List<TransactionOccurrence>>?> GetTransactionsForMonthAsync(
        Guid accountId, Guid userId, int year, int month)
    {
        var account = await _accountRepository.GetByIdAsync(accountId);
        if (account == null || account.UserId != userId)
            return null;

        var startDate = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var endDate = startDate.AddMonths(1).AddSeconds(-1);

        var transactions = await BuildTransactionListForRangeAsync(
            accountId, startDate, endDate, includeProjectedRecurring: true);

        return transactions
            .GroupBy(t => t.StartDate.UtcDateTime.Date)
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => new TransactionOccurrence
                {
                    TransactionId = t.TransactionId,
                    Description = t.Description,
                    Amount = t.Amount,
                    Category = t.Category ?? string.Empty,
                    OccurrenceDate = t.StartDate.DateTime,
                    Frequency = FrequencyType.Once,
                    Color = t.Color
                })
                .OrderBy(t => t.OccurrenceDate)
                .ToList());
    }

    public async Task<List<DailyBalanceSnapshot>?> GetRollingBalanceSnapshotsAsync(
        Guid accountId, Guid userId, DateTimeOffset startDate, DateTimeOffset endDate)
    {
        var projection = await GetEnhancedCashFlowProjectionAsync(
            accountId, userId, startDate, endDate, includeProjectedRecurring: true);

        return projection?.DailySnapshots;
    }

    public async Task<decimal?> GetBalanceAtDateAsync(
        Guid accountId, Guid userId, DateTimeOffset asOfDate)
    {
        var account = await _accountRepository.GetByIdAsync(accountId);
        if (account == null || account.UserId != userId)
            return null;

        return await CalculateBalanceAtDateInternalAsync(accountId, asOfDate, account.InitialBalance);
    }

    public Task<decimal?> GetDailyBalanceAsync(Guid accountId, Guid userId, DateTimeOffset date)
        => GetBalanceAtDateAsync(accountId, userId, date);

    public async Task<List<TransactionWithBalanceDto>?> GetTransactionsWithRunningBalancesAsync(
        Guid accountId, Guid userId, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
    {
        var account = await _accountRepository.GetByIdAsync(accountId);
        if (account == null || account.UserId != userId)
            return null;

        var start = startDate ?? new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = endDate ?? DateTimeOffset.MaxValue;

        var projection = await GetEnhancedCashFlowProjectionAsync(
            accountId, userId, start, end, includeProjectedRecurring: false);

        return projection?.Transactions;
    }

    public decimal CalculateBalanceFromTransactions(
        IEnumerable<Transaction> allTransactions, decimal initialBalance, DateTimeOffset asOfDate)
    {
        var processedTransactions = new List<Transaction>();

        var (recurring, oneTime) = SplitTransactions(allTransactions);

        // One-time transactions that have already occurred
        processedTransactions.AddRange(oneTime.Where(t => t.StartDate <= asOfDate));

        foreach (var template in recurring)
        {
            if (template.StartDate <= asOfDate)
                processedTransactions.Add(template);

            var occurrences = _recurrenceService.GenerateTransactionOccurrences(
                template, template.StartDate, asOfDate);
            processedTransactions.AddRange(occurrences);
        }

        return processedTransactions
            .OrderBy(t => t.StartDate)
            .ThenBy(t => t.CreatedAt)
            .Aggregate(initialBalance, (bal, t) => bal + t.Amount);
    }

    private async Task<List<Transaction>> BuildTransactionListForRangeAsync(
        Guid accountId, DateTimeOffset startDate, DateTimeOffset endDate,
        bool includeProjectedRecurring)
    {
        var allTransactions = await _transactionRepository.GetByAccountAndDateRangeAsync(
            accountId, startDate, endDate);

        if (!includeProjectedRecurring)
            return allTransactions.ToList();

        var (recurring, oneTime) = SplitTransactions(allTransactions);

        var result = new List<Transaction>(
            oneTime.Where(t => t.StartDate >= startDate && t.StartDate <= endDate));

        foreach (var template in recurring)
        {
            if (template.StartDate >= startDate && template.StartDate <= endDate)
                result.Add(template);

            result.AddRange(_recurrenceService.GenerateTransactionOccurrences(
                template, startDate, endDate));
        }

        return result;
    }

    private async Task<decimal> CalculateBalanceAtDateInternalAsync(
        Guid accountId, DateTimeOffset asOfDate, decimal initialBalance)
    {
        var allTransactions = await _transactionRepository.GetByAccountUpToDateAsync(accountId, asOfDate);
        return CalculateBalanceFromTransactions(allTransactions, initialBalance, asOfDate);
    }

    private static (List<Transaction> Recurring, List<Transaction> OneTime) SplitTransactions(
        IEnumerable<Transaction> transactions)
    {
        var recurring = new List<Transaction>();
        var oneTime = new List<Transaction>();

        foreach (var t in transactions)
        {
            if (t.Frequency != FrequencyType.Once)
                recurring.Add(t);
            else
                oneTime.Add(t);
        }

        return (recurring, oneTime);
    }

    private EnhancedCashFlowProjection BuildProjection(
        Core.Entities.Account account,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        decimal startingBalance,
        List<Transaction> transactions)
    {
        var sorted = transactions
            .OrderBy(t => t.StartDate)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        var runningBalance = startingBalance;
        var transactionsWithBalance = new List<TransactionWithBalanceDto>(sorted.Count);
        var dailySnapshots = new Dictionary<DateTime, DailyBalanceSnapshot>();
        decimal totalIncome = 0, totalExpenses = 0;

        foreach (var t in sorted)
        {
            runningBalance += t.Amount;

            if (t.Amount >= 0)
                totalIncome += t.Amount;
            else
                totalExpenses += Math.Abs(t.Amount);

            transactionsWithBalance.Add(new TransactionWithBalanceDto
            {
                TransactionId = t.TransactionId,
                AccountId = t.AccountId,
                Description = t.Description,
                Amount = t.Amount,
                StartDate = t.StartDate,
                Category = t.Category,
                RunningBalance = runningBalance,
                BalanceChange = t.Amount,
                Color = t.Color
            });

            var dateKey = t.StartDate.UtcDateTime.Date;
            if (!dailySnapshots.TryGetValue(dateKey, out var snapshot))
            {
                snapshot = new DailyBalanceSnapshot
                {
                    Date = dateKey,
                    StartOfDayBalance = runningBalance - t.Amount,
                    LowestBalance = runningBalance - t.Amount
                };
                dailySnapshots[dateKey] = snapshot;
            }

            snapshot.TransactionCount++;
            snapshot.EndOfDayBalance = runningBalance;

            if (t.Amount >= 0) snapshot.DayIncome += t.Amount;
            else snapshot.DayExpenses += Math.Abs(t.Amount);

            snapshot.DayChange = snapshot.EndOfDayBalance - snapshot.StartOfDayBalance;

            if (runningBalance < snapshot.LowestBalance)
                snapshot.LowestBalance = runningBalance;
            // HasNegativeBalance evaluated after all transactions — see below.
        }

        // Evaluate HasNegativeBalance once per snapshot using the final EndOfDayBalance,
        // so intraday dips that recover by end of day do not flag the day as negative.
        foreach (var s in dailySnapshots.Values)
            s.HasNegativeBalance = s.EndOfDayBalance < 0;

        var allDailySnapshots = new List<DailyBalanceSnapshot>();
        var currentBalance = startingBalance;

        for (var date = startDate.UtcDateTime.Date; date <= endDate.UtcDateTime.Date; date = date.AddDays(1))
        {
            if (dailySnapshots.TryGetValue(date, out var snapshot))
            {
                allDailySnapshots.Add(snapshot);
                currentBalance = snapshot.EndOfDayBalance;
            }
            else
            {
                allDailySnapshots.Add(new DailyBalanceSnapshot
                {
                    Date = date,
                    StartOfDayBalance = currentBalance,
                    EndOfDayBalance = currentBalance,
                    DayChange = 0,
                    DayIncome = 0,
                    DayExpenses = 0,
                    TransactionCount = 0,
                    LowestBalance = currentBalance,
                    HasNegativeBalance = currentBalance < 0
                });
            }
        }

        return new EnhancedCashFlowProjection
        {
            AccountId = account.AccountId,
            AccountName = account.AccountName,
            StartDate = startDate.DateTime,
            EndDate = endDate.DateTime,
            StartingBalance = startingBalance,
            EndingBalance = runningBalance,
            NetChange = runningBalance - startingBalance,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            DailySnapshots = allDailySnapshots,
            Transactions = transactionsWithBalance
        };
    }

    private static List<CategoryBreakdown> CalculateCategoryBreakdowns(
        List<TransactionWithBalanceDto> transactions)
    {
        var groups = transactions
            .GroupBy(t => new { t.Category, Type = t.Amount >= 0 ? "income" : "expense" })
            .Select(g => new
            {
                g.Key.Category,
                g.Key.Type,
                Total = Math.Abs(g.Sum(t => t.Amount)),
                Count = g.Count()
            })
            .ToList();

        var totalIncome = groups.Where(g => g.Type == "income").Sum(g => g.Total);
        var totalExpenses = groups.Where(g => g.Type == "expense").Sum(g => g.Total);

        return groups.Select(g => new CategoryBreakdown
        {
            Category = g.Category ?? string.Empty,
            Type = g.Type,
            Total = g.Total,
            TransactionCount = g.Count,
            PercentageOfTotal = g.Type == "income"
                ? (totalIncome > 0 ? g.Total / totalIncome * 100 : 0)
                : (totalExpenses > 0 ? g.Total / totalExpenses * 100 : 0)
        }).ToList();
    }
}