using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HexFund.Infrastructure.Data;

/// <summary>
/// Npgsql only accepts DateTimeOffset values with a zero offset for timestamptz;
/// this normalises everything to UTC on the way in and out.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(v => v.ToUniversalTime(), v => v.ToUniversalTime())
    {
    }
}
