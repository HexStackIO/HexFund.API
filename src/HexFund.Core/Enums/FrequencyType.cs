namespace HexFund.Core.Enums;

/// <summary>
/// How often a transaction repeats. The numeric values are stored in the database and sent to
/// clients, so they must not be renumbered.
/// </summary>
public enum FrequencyType
{
    /// <summary>A single occurrence on the start date.</summary>
    Once = 0,

    Daily = 1,

    /// <summary>Every 7 days from the start date.</summary>
    Weekly = 2,

    /// <summary>Every 14 days from the start date.</summary>
    BiWeekly = 3,

    /// <summary>The first and third Friday of each month.</summary>
    FirstThirdFriday = 4,

    /// <summary>Same day of the month, clamped to the last day in shorter months.</summary>
    Monthly = 5,

    /// <summary>Every second month, same day-of-month rule as <see cref="Monthly"/>.</summary>
    BiMonthly = 6
}
