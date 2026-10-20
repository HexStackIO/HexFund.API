using System.Globalization;
using HexFund.Application.Calculation;
using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.Core.Entities;
using HexFund.Core.Interfaces;

namespace HexFund.Application.Services;

public class CashFlowService : ICashFlowService
{
    private static readonly TimeSpan MonthlyOverviewCacheDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan OccurrencesCacheDuration = TimeSpan.FromMinutes(2);

    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly ICacheService _cache;

    public CashFlowService(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        ICacheService cache)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _cache = cache;
    }

    public async Task<EnhancedCashFlowProjection?> GetCashFlowProjectionAsync(
        Guid accountId, Guid userId, DateTimeOffset startDate, DateTimeOffset endDate)
    {
        var account = await _accountRepository.GetByIdForUserAsync(userId, accountId);
        if (account == null)
            return null;

        // One query serves both the opening balance and the in-range occurrences.
        var transactions = await _transactionRepository.GetByAccountUpToDateAsync(accountId, endDate);

        var startingBalance = BalanceCalculator.BalanceAt(
            account.InitialBalance, transactions, startDate.AddTicks(-1));

        var occurrences = RecurrenceCalculator.Expand(transactions, startDate, endDate);
        return BuildProjection(account, startDate, endDate, startingBalance, occurrences);
    }

    // Cache keys include the user id so one user can never be served another user's entry.
    public Task<EnhancedMonthlyOverview?> GetMonthlyOverviewAsync(
        Guid accountId, Guid userId, int year, int month) =>
        _cache.GetOrCreateAsync(
            $"overview:{userId}:{accountId}:{year:D4}-{month:D2}",
            () => BuildMonthlyOverviewAsync(accountId, userId, year, month),
            MonthlyOverviewCacheDuration,
            accountId);

    public Task<List<TransactionOccurrence>?> GetTransactionsForDateAsync(
        Guid accountId, Guid userId, DateOnly date) =>
        _cache.GetOrCreateAsync(
            $"day:{userId}:{accountId}:{date:yyyy-MM-dd}",
            async () =>
            {
                if (await _accountRepository.GetByIdForUserAsync(userId, accountId) == null)
                    return null;

                var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var occurrences = await LoadOccurrencesAsync(accountId, start, start.AddDays(1).AddTicks(-1));
                return occurrences.Select(ToOccurrenceDto).ToList();
            },
            OccurrencesCacheDuration,
            accountId);

    public Task<Dictionary<string, List<TransactionOccurrence>>?> GetTransactionsForMonthAsync(
        Guid accountId, Guid userId, int year, int month) =>
        _cache.GetOrCreateAsync(
            $"month:{userId}:{accountId}:{year:D4}-{month:D2}",
            async () =>
            {
                if (await _accountRepository.GetByIdForUserAsync(userId, accountId) == null)
                    return null;

                var start = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
                var occurrences = await LoadOccurrencesAsync(accountId, start, start.AddMonths(1).AddTicks(-1));

                return occurrences
                    .GroupBy(o => o.Date.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .ToDictionary(g => g.Key, g => g.Select(ToOccurrenceDto).ToList());
            },
            OccurrencesCacheDuration,
            accountId);

    private async Task<EnhancedMonthlyOverview?> BuildMonthlyOverviewAsync(
        Guid accountId, Guid userId, int year, int month)
    {
        var start = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var projection = await GetCashFlowProjectionAsync(
            accountId, userId, start, start.AddMonths(1).AddTicks(-1));

        if (projection == null)
            return null;

        var days = projection.DailySnapshots;

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
            AverageDailyBalance = days.Count > 0 ? days.Average(s => s.EndOfDayBalance) : 0,
            HighestBalance = days.Count > 0 ? days.Max(s => s.EndOfDayBalance) : projection.StartingBalance,
            LowestBalance = days.Count > 0 ? days.Min(s => s.LowestBalance) : projection.StartingBalance,
            DaysWithNegativeBalance = days.Count(s => s.HasNegativeBalance),
            DailyBreakdown = days,
            CategoryBreakdowns = CalculateCategoryBreakdowns(projection.Transactions)
        };
    }

    private async Task<List<Occurrence>> LoadOccurrencesAsync(
        Guid accountId, DateTimeOffset start, DateTimeOffset end)
    {
        var transactions = await _transactionRepository.GetByAccountAndDateRangeAsync(accountId, start, end);
        return RecurrenceCalculator.Expand(transactions, start, end);
    }

    private static TransactionOccurrence ToOccurrenceDto(Occurrence o) => new()
    {
        TransactionId = o.Source.TransactionId,
        Description = o.Source.Description,
        Amount = o.Source.Amount,
        Category = o.Source.Category ?? string.Empty,
        OccurrenceDate = o.Date.UtcDateTime,
        Frequency = o.Source.Frequency,
        Color = o.Source.Color
    };

    private static EnhancedCashFlowProjection BuildProjection(
        Account account,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        decimal startingBalance,
        List<Occurrence> occurrences)
    {
        var runningBalance = startingBalance;
        var transactions = new List<TransactionWithBalanceDto>(occurrences.Count);
        var snapshotsByDate = new Dictionary<DateTime, DailyBalanceSnapshot>();
        decimal totalIncome = 0, totalExpenses = 0;

        foreach (var occurrence in occurrences)
        {
            var t = occurrence.Source;
            var before = runningBalance;
            runningBalance += t.Amount;

            if (t.Amount >= 0) totalIncome += t.Amount;
            else totalExpenses += -t.Amount;

            transactions.Add(new TransactionWithBalanceDto
            {
                TransactionId = t.TransactionId,
                AccountId = t.AccountId,
                Description = t.Description,
                Amount = t.Amount,
                StartDate = occurrence.Date,
                Category = t.Category,
                RunningBalance = runningBalance,
                BalanceChange = t.Amount,
                Color = t.Color
            });

            var day = occurrence.Date.UtcDateTime.Date;
            if (!snapshotsByDate.TryGetValue(day, out var snapshot))
            {
                snapshot = new DailyBalanceSnapshot
                {
                    Date = day,
                    StartOfDayBalance = before,
                    LowestBalance = before
                };
                snapshotsByDate[day] = snapshot;
            }

            snapshot.TransactionCount++;
            snapshot.EndOfDayBalance = runningBalance;
            snapshot.DayChange = runningBalance - snapshot.StartOfDayBalance;
            if (t.Amount >= 0) snapshot.DayIncome += t.Amount;
            else snapshot.DayExpenses += -t.Amount;
            if (runningBalance < snapshot.LowestBalance)
                snapshot.LowestBalance = runningBalance;
        }

        // A day is "negative" by its closing balance, so an intraday dip that recovers is not flagged.
        foreach (var snapshot in snapshotsByDate.Values)
            snapshot.HasNegativeBalance = snapshot.EndOfDayBalance < 0;

        // One snapshot per calendar day in range; quiet days carry the previous close forward.
        var dailySnapshots = new List<DailyBalanceSnapshot>();
        var carried = startingBalance;

        for (var day = startDate.UtcDateTime.Date; day <= endDate.UtcDateTime.Date; day = day.AddDays(1))
        {
            if (snapshotsByDate.TryGetValue(day, out var snapshot))
            {
                dailySnapshots.Add(snapshot);
                carried = snapshot.EndOfDayBalance;
            }
            else
            {
                dailySnapshots.Add(new DailyBalanceSnapshot
                {
                    Date = day,
                    StartOfDayBalance = carried,
                    EndOfDayBalance = carried,
                    LowestBalance = carried,
                    HasNegativeBalance = carried < 0
                });
            }
        }

        return new EnhancedCashFlowProjection
        {
            AccountId = account.AccountId,
            AccountName = account.AccountName,
            StartDate = startDate,
            EndDate = endDate,
            StartingBalance = startingBalance,
            EndingBalance = runningBalance,
            NetChange = runningBalance - startingBalance,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            DailySnapshots = dailySnapshots,
            Transactions = transactions
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

        return groups.Select(g =>
        {
            var typeTotal = g.Type == "income" ? totalIncome : totalExpenses;
            return new CategoryBreakdown
            {
                Category = g.Category ?? string.Empty,
                Type = g.Type,
                Total = g.Total,
                TransactionCount = g.Count,
                PercentageOfTotal = typeTotal > 0 ? g.Total / typeTotal * 100 : 0
            };
        }).ToList();
    }
}
