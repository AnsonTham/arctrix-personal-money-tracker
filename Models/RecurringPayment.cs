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

    /// <summary>Day of month (1-31) this payment is due; clamped to the shorter months.</summary>
    public int DayOfMonth { get; set; } = 1;

    public DateTime NextDueDate { get; set; } = DateTime.Now;

    public DateTime? LastRunDate { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string Notes { get; set; } = string.Empty;
}
