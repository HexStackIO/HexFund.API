using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HexFund.Infrastructure.Data
{

    public sealed class UtcDateTimeOffsetOffsetConverter
        : ValueConverter<DateTimeOffset, DateTimeOffset>
    {
        public UtcDateTimeOffsetOffsetConverter()
        : base(
            v => v.ToUniversalTime(), // to database
            v => v.ToUniversalTime()  // from database
        )
        {
        }
    }
}