using HexFund.Core.Entities;
using HexFund.Core.Enums;

namespace HexFund.Tests;

internal static class TestData
{
    public static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0) =>
        new(y, m, d, h, min, 0, TimeSpan.Zero);

    public static Transaction Tx(
        FrequencyType frequency, DateTimeOffset start, decimal amount = -10m,
        DateTimeOffset? end = null, bool active = true, Guid? accountId = null) => new()
    {
        TransactionId = Guid.NewGuid(),
        AccountId = accountId ?? Guid.NewGuid(),
        Description = "test",
        Amount = amount,
        Frequency = frequency,
        StartDate = start,
        EndDate = end,
        IsActive = active,
        CreatedAt = start
    };
}
