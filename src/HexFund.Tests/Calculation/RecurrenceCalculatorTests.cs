using HexFund.Application.Calculation;
using HexFund.Core.Enums;
using static HexFund.Tests.TestData;

namespace HexFund.Tests.Calculation;

public class RecurrenceCalculatorTests
{
    private static List<DateTimeOffset> Dates(
        FrequencyType f, DateTimeOffset start, DateTimeOffset from, DateTimeOffset to, DateTimeOffset? end = null) =>
        RecurrenceCalculator.GetOccurrenceDates(Tx(f, start, end: end), from, to).ToList();

    [Fact]
    public void Once_IsReturnedOnlyWhenInsideWindow()
    {
        var start = Utc(2026, 3, 5);
        Assert.Single(Dates(FrequencyType.Once, start, Utc(2026, 3, 1), Utc(2026, 3, 31)));
        Assert.Empty(Dates(FrequencyType.Once, start, Utc(2026, 4, 1), Utc(2026, 4, 30)));
        Assert.Empty(Dates(FrequencyType.Once, start, Utc(2026, 2, 1), Utc(2026, 2, 28)));
    }

    [Fact]
    public void Daily_JumpsStraightToTheWindow()
    {
        var dates = Dates(FrequencyType.Daily, Utc(2020, 1, 1), Utc(2025, 3, 1), Utc(2025, 3, 3, 23, 59));
        Assert.Equal(new[] { Utc(2025, 3, 1), Utc(2025, 3, 2), Utc(2025, 3, 3) }, dates);
    }

    [Fact]
    public void Daily_KeepsTimeOfDayAndRespectsWindowStartMidDay()
    {
        var dates = Dates(FrequencyType.Daily, Utc(2026, 1, 1, 14, 30), Utc(2026, 1, 3, 15, 0), Utc(2026, 1, 5, 23, 59));
        Assert.Equal(new[] { Utc(2026, 1, 4, 14, 30), Utc(2026, 1, 5, 14, 30) }, dates);
    }

    [Fact]
    public void Weekly_And_BiWeekly_StepFromStartDate()
    {
        var start = Utc(2026, 1, 1);
        Assert.Equal(
            new[] { Utc(2026, 1, 1), Utc(2026, 1, 8), Utc(2026, 1, 15) },
            Dates(FrequencyType.Weekly, start, start, Utc(2026, 1, 20)));
        Assert.Equal(
            new[] { Utc(2026, 1, 1), Utc(2026, 1, 15), Utc(2026, 1, 29) },
            Dates(FrequencyType.BiWeekly, start, start, Utc(2026, 1, 31)));
    }

    [Fact]
    public void Monthly_ClampsToShortMonthsAndRecoversAfterwards()
    {
        var dates = Dates(FrequencyType.Monthly, Utc(2025, 1, 31), Utc(2025, 1, 1), Utc(2025, 4, 30));
        Assert.Equal(new[] { Utc(2025, 1, 31), Utc(2025, 2, 28), Utc(2025, 3, 31), Utc(2025, 4, 30) }, dates);
    }

    [Fact]
    public void Monthly_HonoursLeapYear_AndKeepsTimeOfDay()
    {
        var dates = Dates(FrequencyType.Monthly, Utc(2024, 1, 31, 9, 15), Utc(2024, 2, 1), Utc(2024, 2, 29, 23, 59));
        Assert.Equal(new[] { Utc(2024, 2, 29, 9, 15) }, dates);
    }

    [Fact]
    public void BiMonthly_StepsTwoMonths()
    {
        var dates = Dates(FrequencyType.BiMonthly, Utc(2026, 1, 15), Utc(2026, 1, 1), Utc(2026, 8, 1));
        Assert.Equal(new[] { Utc(2026, 1, 15), Utc(2026, 3, 15), Utc(2026, 5, 15), Utc(2026, 7, 15) }, dates);
    }

    [Fact]
    public void Monthly_DoesNotEmitBeforeStart_WhenWindowIsEarlier()
    {
        Assert.Empty(Dates(FrequencyType.Monthly, Utc(2026, 6, 10), Utc(2026, 1, 1), Utc(2026, 5, 31)));
    }

    [Fact]
    public void EndDate_IsInclusiveAndStopsTheSeries()
    {
        var dates = Dates(FrequencyType.Weekly, Utc(2026, 1, 1), Utc(2026, 1, 1), Utc(2026, 12, 31), end: Utc(2026, 1, 15));
        Assert.Equal(new[] { Utc(2026, 1, 1), Utc(2026, 1, 8), Utc(2026, 1, 15) }, dates);
    }

    [Fact]
    public void FirstThirdFriday_IncludesStart_ThenFirstAndThirdFridays()
    {
        // 2026-01-01 is a Thursday; first Friday Jan 2, third Friday Jan 16.
        var dates = Dates(FrequencyType.FirstThirdFriday, Utc(2026, 1, 1), Utc(2026, 1, 1), Utc(2026, 1, 31));
        Assert.Equal(new[] { Utc(2026, 1, 1), Utc(2026, 1, 2), Utc(2026, 1, 16) }, dates);
    }

    [Fact]
    public void FirstThirdFriday_WindowStartingAfterFirstFriday_StillFindsThirdFriday()
    {
        // March 2026: first Friday is the 6th, third Friday the 20th.
        var dates = Dates(FrequencyType.FirstThirdFriday, Utc(2026, 1, 1), Utc(2026, 3, 10), Utc(2026, 3, 31));
        Assert.Equal(new[] { Utc(2026, 3, 20) }, dates);
    }

    [Fact]
    public void FirstThirdFriday_NeverEmitsBeforeStartDate()
    {
        // Start on Jan 10 (after the first Friday, before the third).
        var dates = Dates(FrequencyType.FirstThirdFriday, Utc(2026, 1, 10), Utc(2026, 1, 1), Utc(2026, 1, 31));
        Assert.Equal(new[] { Utc(2026, 1, 10), Utc(2026, 1, 16) }, dates);
    }

    [Fact]
    public void FirstThirdFriday_StartingOnAFriday_IsNotDuplicated()
    {
        var dates = Dates(FrequencyType.FirstThirdFriday, Utc(2026, 1, 2), Utc(2026, 1, 1), Utc(2026, 1, 31));
        Assert.Equal(new[] { Utc(2026, 1, 2), Utc(2026, 1, 16) }, dates);
    }

    [Fact]
    public void Expand_OrdersAcrossTemplatesByDate()
    {
        var a = Tx(FrequencyType.Weekly, Utc(2026, 1, 1), 5m);
        var b = Tx(FrequencyType.Once, Utc(2026, 1, 4), -3m);
        var result = RecurrenceCalculator.Expand(new[] { a, b }, Utc(2026, 1, 1), Utc(2026, 1, 10));

        Assert.Equal(new[] { Utc(2026, 1, 1), Utc(2026, 1, 4), Utc(2026, 1, 8) }, result.Select(o => o.Date));
    }
}
