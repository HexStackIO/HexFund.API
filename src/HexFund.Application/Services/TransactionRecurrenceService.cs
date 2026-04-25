using HexFund.Core.Entities;
using HexFund.Core.Enums;

namespace HexFund.Application.Services;

public class TransactionRecurrenceService
{
    /// <summary>
    /// Returns projected Transaction entities (not persisted) for each recurrence of
    /// <paramref name="template"/> that falls within [startDate, endDate], excluding
    /// the template's own StartDate occurrence.
    /// </summary>
    public List<Transaction> GenerateTransactionOccurrences(
        Transaction template,
        DateTimeOffset startDate,
        DateTimeOffset endDate)
    {
        var occurrences = new List<Transaction>();

        var effectiveEnd = template.EndDate.HasValue && template.EndDate.Value < endDate
            ? template.EndDate.Value : endDate;

        if (template.StartDate > effectiveEnd)
            return occurrences;

        switch (template.Frequency)
        {
            case FrequencyType.Once:
                break; // No additional occurrences for one-time transactions

            case FrequencyType.Daily:
                AddOccurrences(occurrences, template, startDate, effectiveEnd, d => d.AddDays(1));
                break;

            case FrequencyType.Weekly:
                AddOccurrences(occurrences, template, startDate, effectiveEnd, d => d.AddDays(7));
                break;

            case FrequencyType.BiWeekly:
                AddOccurrences(occurrences, template, startDate, effectiveEnd, d => d.AddDays(14));
                break;

            case FrequencyType.Monthly:
                AddMonthlyOccurrences(occurrences, template, startDate, effectiveEnd, stepMonths: 1);
                break;

            case FrequencyType.BiMonthly:
                AddMonthlyOccurrences(occurrences, template, startDate, effectiveEnd, stepMonths: 2);
                break;

            case FrequencyType.FirstThirdFriday:
                occurrences.AddRange(GenerateFirstThirdFridayOccurrences(template, startDate, effectiveEnd));
                break;
        }

        return occurrences;
    }

    private void AddOccurrences(
        List<Transaction> list,
        Transaction template,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        Func<DateTimeOffset, DateTimeOffset> advance)
    {
        var current = template.StartDate;
        while (current <= windowEnd)
        {
            if (current >= windowStart && current.Date != template.StartDate.Date)
                list.Add(CreateOccurrence(template, current));
            current = advance(current);
        }
    }

    private void AddMonthlyOccurrences(
        List<Transaction> list,
        Transaction template,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int stepMonths)
    {
        var originalDay = template.StartDate.Day;
        var currentMonth = new DateTimeOffset(template.StartDate.Year, template.StartDate.Month, 1,
            0, 0, 0, TimeSpan.Zero);

        while (true)
        {
            var daysInMonth = DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month);
            var day = Math.Min(originalDay, daysInMonth);
            var occurrenceDate = new DateTimeOffset(currentMonth.Year, currentMonth.Month, day,
                0, 0, 0, TimeSpan.Zero);

            if (occurrenceDate > windowEnd) break;

            if (occurrenceDate >= windowStart && occurrenceDate.Date != template.StartDate.Date)
                list.Add(CreateOccurrence(template, occurrenceDate));

            currentMonth = currentMonth.AddMonths(stepMonths);
        }
    }

    private List<Transaction> GenerateFirstThirdFridayOccurrences(
        Transaction template, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var occurrences = new List<Transaction>();
        var current = windowStart;

        while (current <= windowEnd)
        {
            if (current.DayOfWeek == DayOfWeek.Friday)
            {
                // First Friday of the month: previous Friday was in the prior month
                bool isFirstFriday = current.Month > current.AddDays(-7).Month
                                  || current.Year > current.AddDays(-7).Year;

                if (isFirstFriday)
                {
                    if (current.Date != template.StartDate.Date)
                        occurrences.Add(CreateOccurrence(template, current));

                    var thirdFriday = current.AddDays(14);
                    if (thirdFriday <= windowEnd && thirdFriday.Date != template.StartDate.Date)
                        occurrences.Add(CreateOccurrence(template, thirdFriday));
                }
            }
            current = current.AddDays(1);
        }

        return occurrences;
    }

    private static Transaction CreateOccurrence(Transaction template, DateTimeOffset occurrenceDate) => new()
    {
        TransactionId = Guid.NewGuid(),
        AccountId = template.AccountId,
        Description = template.Description,
        Amount = template.Amount,
        Category = template.Category,
        Frequency = template.Frequency,
        StartDate = occurrenceDate,
        EndDate = template.EndDate,
        IsActive = true,
        Color = template.Color,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
