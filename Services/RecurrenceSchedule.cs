using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>
/// Works out when a recurring payment falls due. Pure date arithmetic, kept apart from the service
/// that posts the transactions so it can be reasoned about - and tested - on its own.
///
/// Two kinds of schedule:
///   - A fixed day number, clamped to shorter months: the 31st is 30 Sep but still 31 Oct.
///   - A weekday position, such as the last Friday, which lands on a different date each month
///     (25 Sep, then 30 Oct, then 27 Nov) with no adjusting by hand.
///   - The last working day of the month, stepping back over weekends and the public holidays the
///     user keeps in Settings: August 2026 pays on Friday the 28th, because the 31st is Merdeka Day
///     and the 29th and 30th are the weekend.
///
/// Holidays are passed in rather than looked up here, so this stays pure date arithmetic.
/// </summary>
public static class RecurrenceSchedule
{
    /// <summary>
    /// The first date on or after <paramref name="from"/> that the payment's rule falls on. Used
    /// when a payment is created, to set its first due date.
    /// </summary>
    public static DateTime FirstOnOrAfter(RecurringPayment payment, DateTime from, IReadOnlySet<DateTime>? holidays = null)
    {
        var start = from.Date;
        var candidate = InMonth(payment, start.Year, start.Month, holidays);
        if (candidate >= start)
            return candidate;

        var next = start.AddMonths(1);
        return InMonth(payment, next.Year, next.Month, holidays);
    }

    /// <summary>
    /// The next due date after an occurrence on <paramref name="dueDate"/> - always in the
    /// following month, so a schedule advances exactly one occurrence at a time.
    /// </summary>
    public static DateTime Next(RecurringPayment payment, DateTime dueDate, IReadOnlySet<DateTime>? holidays = null)
    {
        // Step by month from the first of the month, not from the date itself: the 31st plus a
        // month would skip February entirely.
        var next = new DateTime(dueDate.Year, dueDate.Month, 1).AddMonths(1);
        var candidate = InMonth(payment, next.Year, next.Month, holidays);

        // A weekday or working-day rule can land earlier in the month than the date just posted
        // (30 Oct, then 27 Nov). It must still be after it, or the same occurrence posts twice.
        if (candidate <= dueDate.Date)
        {
            var after = next.AddMonths(1);
            candidate = InMonth(payment, after.Year, after.Month, holidays);
        }

        return candidate;
    }

    /// <summary>The date this payment falls on within one specific month.</summary>
    public static DateTime InMonth(RecurringPayment payment, int year, int month, IReadOnlySet<DateTime>? holidays = null) =>
        payment.RuleType switch
        {
            RecurrenceRuleType.NthWeekdayOfMonth => WeekdayInMonth(year, month, payment.Weekday, payment.Occurrence),
            RecurrenceRuleType.LastWorkingDayOfMonth => LastWorkingDayInMonth(year, month, holidays),
            _ => OnDay(year, month, payment.DayOfMonth)
        };

    /// <summary>
    /// The last day of the month that is worked: start at the last calendar day and walk back a day
    /// at a time over Saturdays, Sundays and listed holidays. Walking back rather than jumping a
    /// fixed number of days is what handles a holiday that falls next to a weekend.
    /// </summary>
    public static DateTime LastWorkingDayInMonth(int year, int month, IReadOnlySet<DateTime>? holidays = null)
    {
        var day = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var firstOfMonth = new DateTime(year, month, 1);

        while (day >= firstOfMonth && !IsWorkingDay(day, holidays))
            day = day.AddDays(-1);

        // A month entirely of non-working days can't happen, but never hand back a date outside it.
        return day < firstOfMonth ? new DateTime(year, month, DateTime.DaysInMonth(year, month)) : day;
    }

    public static bool IsWorkingDay(DateTime date, IReadOnlySet<DateTime>? holidays = null) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && (holidays is null || !holidays.Contains(date.Date));

    /// <summary>
    /// The <paramref name="occurrence"/> <paramref name="weekday"/> of a month, e.g. the last
    /// Friday. Counting from the end for Last means a month with five Fridays gives the fifth.
    /// </summary>
    public static DateTime WeekdayInMonth(int year, int month, DayOfWeek weekday, MonthlyOccurrence occurrence)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);

        if (occurrence == MonthlyOccurrence.Last)
        {
            var last = new DateTime(year, month, daysInMonth);
            var back = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
            return last.AddDays(-back);
        }

        var first = new DateTime(year, month, 1);
        var forward = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        var day = 1 + forward + (7 * (int)occurrence);

        // A month without, say, a fifth Monday falls back to the last one it does have.
        return day <= daysInMonth
            ? new DateTime(year, month, day)
            : new DateTime(year, month, day - 7);
    }

    /// <summary>A day number within a month, clamped to that month's length.</summary>
    public static DateTime OnDay(int year, int month, int dayOfMonth) =>
        new(year, month, Math.Clamp(dayOfMonth, 1, DateTime.DaysInMonth(year, month)));

    /// <summary>How the schedule reads on screen, e.g. "last Friday of each month".</summary>
    public static string Describe(RecurringPayment payment) =>
        payment.RuleType switch
        {
            RecurrenceRuleType.NthWeekdayOfMonth => $"{payment.Occurrence.ToString().ToLowerInvariant()} {payment.Weekday} of each month",
            RecurrenceRuleType.LastWorkingDayOfMonth => "last working day of each month",
            _ => $"{Ordinal(payment.DayOfMonth)} of each month"
        };

    public static string Ordinal(int day) => day switch
    {
        1 or 21 or 31 => $"{day}st",
        2 or 22 => $"{day}nd",
        3 or 23 => $"{day}rd",
        _ => $"{day}th"
    };
}
