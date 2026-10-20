using HexFund.Application.Calculation;
using HexFund.Core.Enums;
using static HexFund.Tests.TestData;

namespace HexFund.Tests.Calculation;

public class BalanceCalculatorTests
{
    [Fact]
    public void RecurringTransactions_AccumulateUpToAsOfDate()
    {
        var rent = Tx(FrequencyType.Monthly, Utc(2026, 1, 1), -1000m);
        var pay = Tx(FrequencyType.BiWeekly, Utc(2026, 1, 2), 500m);

        // Rent: Jan, Feb, Mar. Pay: Jan 2, 16, 30, Feb 13, 27, Mar 13, 27 -> 7 payments.
        var balance = BalanceCalculator.BalanceAt(200m, new[] { rent, pay }, Utc(2026, 3, 31));

        Assert.Equal(200m - 3000m + 3500m, balance);
    }

    [Fact]
    public void InactiveAndFutureTransactions_AreIgnored()
    {
        var inactive = Tx(FrequencyType.Daily, Utc(2026, 1, 1), -50m, active: false);
        var future = Tx(FrequencyType.Once, Utc(2027, 1, 1), -999m);

        Assert.Equal(100m, BalanceCalculator.BalanceAt(100m, new[] { inactive, future }, Utc(2026, 6, 1)));
    }

    [Fact]
    public void EndedSeries_StopsContributing()
    {
        var sub = Tx(FrequencyType.Monthly, Utc(2026, 1, 5), -10m, end: Utc(2026, 3, 5));
        Assert.Equal(100m - 30m, BalanceCalculator.BalanceAt(100m, new[] { sub }, Utc(2026, 12, 31)));
    }

    [Fact]
    public void LongRunningDailySeries_Totals()
    {
        var daily = Tx(FrequencyType.Daily, Utc(2000, 1, 1), -1m);
        var asOf = Utc(2026, 1, 1);
        var days = (decimal)(asOf - Utc(2000, 1, 1)).TotalDays + 1;

        Assert.Equal(-days, BalanceCalculator.BalanceAt(0m, new[] { daily }, asOf));
    }
}
