using HexFund.Core.Entities;
using HexFund.Core.Enums;

namespace HexFund.Application.Calculation;

/// <summary>A single dated occurrence of a transaction (the template itself or a projected repeat).</summary>
public readonly record struct Occurrence(Transaction Source, DateTimeOffset Date);

/// <summary>
/// Pure recurrence engine. Every occurrence is derived from the template's StartDate in UTC,
/// keeps the template's time of day, and includes the template's own StartDate. Nothing is
/// persisted or allocated per occurrence beyond the yielded date, and long-running
/// daily/weekly series jump straight to the requested window instead of walking from the start.
/// </summary>
public static class RecurrenceCalculator
{
    /// <summary>
    /// Occurrence dates of <paramref name="template"/> within [from, to] (inclusive),
    /// bounded by the template's own EndDate, in ascending order.
    /// </summary>
    public static IEnumerable<DateTimeOffset> GetOccurrenceDates(
        Transaction template, DateTimeOffset from, DateTimeOffset to)
    {
        var start = template.StartDate.UtcDateTime;
        var lo = from.UtcDateTime;
        var hi = to.UtcDateTime;

        if (template.EndDate is { } endDate && endDate.UtcDateTime < hi)
            hi = endDate.UtcDateTime;

        if (start > hi)
            return Array.Empty<DateTimeOffset>();

        return template.Frequency switch
        {
            FrequencyType.Once => start >= lo ? new[] { ToOffset(start) } : Array.Empty<DateTimeOffset>(),
            FrequencyType.Daily => FixedStep(start, lo, hi, TimeSpan.FromDays(1)),
            FrequencyType.Weekly => FixedStep(start, lo, hi, TimeSpan.FromDays(7)),
            FrequencyType.BiWeekly => FixedStep(start, lo, hi, TimeSpan.FromDays(14)),
            FrequencyType.Monthly => MonthlyStep(start, lo, hi, 1),
            FrequencyType.BiMonthly => MonthlyStep(start, lo, hi, 2),
            FrequencyType.FirstThirdFriday => FirstThirdFridays(start, lo, hi),
            _ => Array.Empty<DateTimeOffset>()
        };
    }

    /// <summary>Occurrences of every template within [from, to], ordered by date then creation time.</summary>
    public static List<Occurrence> Expand(
        IEnumerable<Transaction> templates, DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<Occurrence>();
        foreach (var template in templates)
            foreach (var date in GetOccurrenceDates(template, from, to))
                result.Add(new Occurrence(template, date));

        result.Sort(static (a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            return byDate != 0 ? byDate : a.Source.CreatedAt.CompareTo(b.Source.CreatedAt);
        });
        return result;
    }

    private static IEnumerable<DateTimeOffset> FixedStep(DateTime start, DateTime lo, DateTime hi, TimeSpan step)
    {
        // Index of the first occurrence at or after lo.
        var skip = lo > start ? (lo.Ticks - start.Ticks + step.Ticks - 1) / step.Ticks : 0;

        for (var n = skip; ; n++)
        {
            var ticks = start.Ticks + n * step.Ticks;
            if (ticks > hi.Ticks || ticks > DateTime.MaxValue.Ticks)
                yield break;

            yield return ToOffset(new DateTime(ticks, DateTimeKind.Utc));
        }
    }

    private static IEnumerable<DateTimeOffset> MonthlyStep(DateTime start, DateTime lo, DateTime hi, int stepMonths)
    {
        var monthsSinceStart = (lo.Year - start.Year) * 12 + (lo.Month - start.Month);
        var first = Math.Max(0, monthsSinceStart / stepMonths - 1);

        for (var k = first; ; k++)
        {
            var monthOffset = k * stepMonths;
            var month = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(monthOffset);

            // Short months clamp to their last day (31st -> 30th/28th).
            var day = Math.Min(start.Day, DateTime.DaysInMonth(month.Year, month.Month));
            var occurrence = new DateTime(month.Year, month.Month, day, 0, 0, 0, DateTimeKind.Utc) + start.TimeOfDay;

            if (occurrence > hi)
                yield break;

            if (occurrence >= lo && occurrence >= start)
                yield return ToOffset(occurrence);
        }
    }

    // The template's own StartDate always counts (even when it is not itself a 1st/3rd Friday);
    // after that, the 1st and 3rd Friday of each month on later dates.
    private static IEnumerable<DateTimeOffset> FirstThirdFridays(DateTime start, DateTime lo, DateTime hi)
    {
        if (start >= lo)
            yield return ToOffset(start);

        var monthsSinceStart = (lo.Year - start.Year) * 12 + (lo.Month - start.Month);

        for (var k = Math.Max(0, monthsSinceStart - 1); ; k++)
        {
            var month = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(k);
            if (month > hi)
                yield break;

            var daysToFriday = ((int)DayOfWeek.Friday - (int)month.DayOfWeek + 7) % 7;
            var firstFriday = month.AddDays(daysToFriday);

            foreach (var friday in new[] { firstFriday, firstFriday.AddDays(14) })
            {
                var occurrence = friday + start.TimeOfDay;
                if (occurrence.Date > start.Date && occurrence >= lo && occurrence <= hi)
                    yield return ToOffset(occurrence);
            }
        }
    }

    private static DateTimeOffset ToOffset(DateTime utc) => new(utc, TimeSpan.Zero);
}
