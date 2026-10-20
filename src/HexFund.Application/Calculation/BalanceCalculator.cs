using HexFund.Core.Entities;

namespace HexFund.Application.Calculation;

public static class BalanceCalculator
{
    /// <summary>
    /// Balance after applying every occurrence (template and projected repeats)
    /// that falls on or before <paramref name="asOf"/>. Inactive transactions are ignored.
    /// </summary>
    public static decimal BalanceAt(
        decimal initialBalance, IEnumerable<Transaction> transactions, DateTimeOffset asOf)
    {
        var balance = initialBalance;

        foreach (var transaction in transactions)
        {
            if (!transaction.IsActive)
                continue;

            foreach (var _ in RecurrenceCalculator.GetOccurrenceDates(transaction, transaction.StartDate, asOf))
                balance += transaction.Amount;
        }

        return balance;
    }
}
