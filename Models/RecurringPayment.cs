using SQLite;

namespace Arctrix.PersonalMoneyTracker.Models;

/// <summary>
/// A monthly recurring income/expense definition (subscription, salary, rent, etc.).
/// The app checks these on launch and posts a matching TransactionRecord once
/// NextDueDate has passed, then advances NextDueDate by one month.
/// </summary>
public class RecurringPayment
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public TransactionType Type { get; set; } = TransactionType.Expense;

    public int AccountId { get; set; }

    public int CategoryId { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "MYR";

    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;

    /// <summary>
    /// How the due date is chosen. Added after the fact, and the original behaviour is the zero
    /// value, so rows saved before this column existed keep their fixed-day schedule untouched.
    /// </summary>
    public RecurrenceRuleType RuleType { get; set; } = RecurrenceRuleType.FixedDayOfMonth;

    /// <summary>Day of month (1-31) this payment is due; clamped to the shorter months. Used by <see cref="RecurrenceRuleType.FixedDayOfMonth"/>.</summary>
    public int DayOfMonth { get; set; } = 1;

    /// <summary>The weekday for <see cref="RecurrenceRuleType.NthWeekdayOfMonth"/>, e.g. Friday.</summary>
    public DayOfWeek Weekday { get; set; } = DayOfWeek.Friday;

    /// <summary>Which <see cref="Weekday"/> in the month, e.g. Last for "last Friday".</summary>
    public MonthlyOccurrence Occurrence { get; set; } = MonthlyOccurrence.Last;

    public DateTime NextDueDate { get; set; } = DateTime.Now;

    public DateTime? LastRunDate { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string Notes { get; set; } = string.Empty;
}
