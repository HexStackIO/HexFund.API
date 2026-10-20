using HexFund.Core.Enums;

namespace HexFund.Application.DTOs;

public class TransactionWithBalanceDto
{
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public string? Category { get; set; }
    public decimal RunningBalance { get; set; }
    public decimal BalanceChange { get; set; }
    public string Type => Amount >= 0 ? "income" : "expense";
    public decimal AbsoluteAmount => Math.Abs(Amount);
    public string? Color { get; set; }
}

public class EnhancedCashFlowProjection
{
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public decimal StartingBalance { get; set; }
    public decimal EndingBalance { get; set; }
    public decimal NetChange { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public List<DailyBalanceSnapshot> DailySnapshots { get; set; } = new();
    public List<TransactionWithBalanceDto> Transactions { get; set; } = new();
}

public class DailyBalanceSnapshot
{
    public DateTime Date { get; set; }
    public decimal EndOfDayBalance { get; set; }
    public decimal StartOfDayBalance { get; set; }
    public decimal DayChange { get; set; }
    public int TransactionCount { get; set; }
    public decimal DayIncome { get; set; }
    public decimal DayExpenses { get; set; }
    public bool HasNegativeBalance { get; set; }
    public decimal LowestBalance { get; set; }
}

/// <summary>One dated occurrence of a (possibly recurring) transaction.</summary>
public class TransactionOccurrence
{
    public Guid TransactionId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime OccurrenceDate { get; set; }
    public FrequencyType Frequency { get; set; }
    public string? Color { get; set; }
}

public class EnhancedMonthlyOverview
{
    public int Year { get; set; }
    public int Month { get; set; }
    public Guid AccountId { get; set; }
    public decimal StartingBalance { get; set; }
    public decimal EndingBalance { get; set; }
    public decimal NetChange { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal AverageDailyBalance { get; set; }
    public decimal HighestBalance { get; set; }
    public decimal LowestBalance { get; set; }
    public int DaysWithNegativeBalance { get; set; }
    public List<DailyBalanceSnapshot> DailyBreakdown { get; set; } = new();
    public List<CategoryBreakdown> CategoryBreakdowns { get; set; } = new();
}

public class CategoryBreakdown
{
    public string Category { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int TransactionCount { get; set; }
    public decimal PercentageOfTotal { get; set; }
}
