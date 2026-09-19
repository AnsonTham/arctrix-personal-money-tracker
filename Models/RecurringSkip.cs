using SQLite;

namespace Arctrix.PersonalMoneyTracker.Models;

/// <summary>
/// One occurrence of a recurring payment that came due but wasn't posted, because the paying
/// account didn't hold enough. The schedule is deliberately left where it is, so the next run
/// retries the same occurrence; this row records what happened in the meantime and stops the
/// same shortfall being reported over and over.
/// </summary>
public class RecurringSkip
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int RecurringPaymentId { get; set; }

    /// <summary>The occurrence this is about; one row per (payment, due date).</summary>
    public DateTime DueDate { get; set; }

    public int AccountId { get; set; }

    /// <summary>What the occurrence would have cost, in the account's own currency.</summary>
    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "MYR";

    /// <summary>Balance at the moment it was skipped, in the same currency.</summary>
    public decimal Balance { get; set; }

    public DateTime FirstSkippedAt { get; set; } = DateTime.Now;

    public DateTime LastSkippedAt { get; set; } = DateTime.Now;

    /// <summary>When the user was last told about it; null until the notification goes out.</summary>
    public DateTime? NotifiedAt { get; set; }

    /// <summary>Set once the occurrence finally posted, which is what retires this row.</summary>
    public DateTime? ResolvedAt { get; set; }

    [Ignore]
    public decimal Shortfall => Amount - Balance;
}
