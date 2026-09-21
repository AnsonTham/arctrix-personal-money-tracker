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
/// </summary>
public static class RecurrenceSchedule
{
    /// <summary>
    /// The first date on or after <paramref name="from"/> that the payment's rule falls on. Used
    /// when a payment is created, to set its first due date.
    /// </summary>
    public static DateTime FirstOnOrAfter(RecurringPayment payment, DateTime from)
    {
        var start = from.Date;
        var candidate = InMonth(payment, start.Year, start.Month);
        if (candidate >= start)
            return candidate;

        var next = start.AddMonths(1);
        return InMonth(payment, next.Year, next.Month);
    }

    /// <summary>
    /// The next due date after an occurrence on <paramref name="dueDate"/> - always in the
    /// following month, so a schedule advances exactly one occurrence at a time.
    /// </summary>
    public static DateTime Next(RecurringPayment payment, DateTime dueDate)
    {
        var next = dueDate.Date.AddMonths(1);
        var candidate = InMonth(payment, next.Year, next.Month);

        // A weekday rule can land earlier in the month than the date just posted (30 Oct, then
        // 27 Nov). It must still be after it, or the same occurrence would be posted twice.
        if (candidate <= dueDate.Date)
        {
            var after = next.AddMonths(1);
            candidate = InMonth(payment, after.Year, after.Month);
        }

        return candidate;
    }

    /// <summary>The date this payment falls on within one specific month.</summary>
    public static DateTime InMonth(RecurringPayment payment, int year, int month) =>
        payment.RuleType == RecurrenceRuleType.NthWeekdayOfMonth
            ? WeekdayInMonth(year, month, payment.Weekday, payment.Occurrence)
            : OnDay(year, month, payment.DayOfMonth);

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
        payment.RuleType == RecurrenceRuleType.NthWeekdayOfMonth
            ? $"{payment.Occurrence.ToString().ToLowerInvariant()} {payment.Weekday} of each month"
            : $"{Ordinal(payment.DayOfMonth)} of each month";

    public static string Ordinal(int day) => day switch
    {
        1 or 21 or 31 => $"{day}st",
        2 or 22 => $"{day}nd",
        3 or 23 => $"{day}rd",
        _ => $"{day}th"
    };
}
