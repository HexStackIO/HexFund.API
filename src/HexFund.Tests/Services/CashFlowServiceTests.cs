using HexFund.Application.Services;
using HexFund.Core.Entities;
using HexFund.Core.Enums;
using static HexFund.Tests.TestData;

namespace HexFund.Tests.Services;

public class CashFlowServiceTests
{
    private readonly FakeAccountRepository _accounts = new();
    private readonly FakeTransactionRepository _transactions;
    private readonly CashFlowService _service;

    private readonly Guid _owner = Guid.NewGuid();
    private readonly Account _account;

    public CashFlowServiceTests()
    {
        _transactions = new FakeTransactionRepository(_accounts);
        _service = new CashFlowService(_accounts, _transactions, new RecordingCache());
        _account = new Account { AccountId = Guid.NewGuid(), UserId = _owner, AccountName = "Main", InitialBalance = 1000m };
        _accounts.Accounts.Add(_account);
    }

    [Fact]
    public async Task MonthlyOverview_ComputesBalancesFromRecurringTransactions()
    {
        _transactions.Transactions.Add(Tx(FrequencyType.Monthly, Utc(2026, 1, 15), -100m, accountId: _account.AccountId));
        _transactions.Transactions.Add(Tx(FrequencyType.Once, Utc(2026, 3, 10), 250m, accountId: _account.AccountId));

        var overview = await _service.GetMonthlyOverviewAsync(_account.AccountId, _owner, 2026, 3);

        Assert.NotNull(overview);
        Assert.Equal(800m, overview!.StartingBalance);   // 1000 - Jan 15 - Feb 15
        Assert.Equal(950m, overview.EndingBalance);      // 800 - 100 + 250
        Assert.Equal(250m, overview.TotalIncome);
        Assert.Equal(100m, overview.TotalExpenses);
        Assert.Equal(31, overview.DailyBreakdown.Count);
        Assert.Equal(950m, overview.DailyBreakdown[^1].EndOfDayBalance);
    }

    [Fact]
    public async Task OtherUsers_NeverReceiveSomeoneElsesCachedData()
    {
        _transactions.Transactions.Add(Tx(FrequencyType.Monthly, Utc(2026, 1, 15), -100m, accountId: _account.AccountId));

        // The owner warms the cache first.
        Assert.NotNull(await _service.GetMonthlyOverviewAsync(_account.AccountId, _owner, 2026, 3));

        var stranger = Guid.NewGuid();
        Assert.Null(await _service.GetMonthlyOverviewAsync(_account.AccountId, stranger, 2026, 3));
        Assert.Null(await _service.GetTransactionsForMonthAsync(_account.AccountId, stranger, 2026, 3));
        Assert.Null(await _service.GetTransactionsForDateAsync(_account.AccountId, stranger, new DateOnly(2026, 3, 15)));
    }

    [Fact]
    public async Task TransactionsForMonth_AreGroupedByDay()
    {
        _transactions.Transactions.Add(Tx(FrequencyType.Weekly, Utc(2026, 3, 2), -5m, accountId: _account.AccountId));

        var byDay = await _service.GetTransactionsForMonthAsync(_account.AccountId, _owner, 2026, 3);

        Assert.Equal(new[] { "2026-03-02", "2026-03-09", "2026-03-16", "2026-03-23", "2026-03-30" },
            byDay!.Keys.OrderBy(k => k));
    }

    [Fact]
    public async Task ProjectionWindow_NeverContainsOccurrencesBeforeTheTransactionStarts()
    {
        _transactions.Transactions.Add(Tx(FrequencyType.FirstThirdFriday, Utc(2026, 3, 10), -5m, accountId: _account.AccountId));

        var byDay = await _service.GetTransactionsForMonthAsync(_account.AccountId, _owner, 2026, 3);

        Assert.DoesNotContain(byDay!.Keys, k => string.CompareOrdinal(k, "2026-03-10") < 0);
        Assert.Contains("2026-03-20", byDay.Keys);
    }
}
