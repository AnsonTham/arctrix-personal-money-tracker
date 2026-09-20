using SQLite;

namespace Arctrix.PersonalMoneyTracker.Models;

/// <summary>
/// A lump sum collected upfront that covers a fixed number of months - friends paying their share
/// of a shared subscription for a year, say.
///
/// This records nothing financial. The money itself was already recorded as an ordinary Income
/// transaction when it arrived, and the subscription it pays for keeps posting as an ordinary
/// recurring Expense. Neither is touched here: this is a reminder of how much of that lump sum is
/// still "spoken for", so it is clear when the next round needs collecting. Nothing in this file
/// creates, moves or adjusts a transaction or a balance.
/// </summary>
public class PrepaidCredit
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The lump sum received, in <see cref="Currency"/>.</summary>
    public decimal TotalAmount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "MYR";

    /// <summary>How many months that sum was meant to cover.</summary>
    public int MonthsCovered { get; set; } = 12;

    /// <summary>The first month it covers; months are counted from here.</summary>
    public DateTime StartDate { get; set; } = DateTime.Today;

    /// <summary>The subscription this pays for, for display only; null when it isn't tied to one.</summary>
    public int? RecurringPaymentId { get; set; }

    /// <summary>Set when the arrangement has ended, which keeps it off the Recurring page.</summary>
    public bool IsClosed { get; set; }

    /// <summary>When the user was last told it had run out, so the reminder isn't repeated endlessly.</summary>
    public DateTime? CollectionNotifiedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Where this credit stands on a given day.</summary>
    public PrepaidCreditStatus StatusOn(DateTime today)
    {
        var months = Math.Max(1, MonthsCovered);

        // Whole months since the start: the 14th to the 13th is not yet a month.
        var elapsed = ((today.Year - StartDate.Year) * 12) + today.Month - StartDate.Month;
        if (today.Day < StartDate.Day)
            elapsed--;
        elapsed = Math.Clamp(elapsed, 0, months);

        var perMonth = TotalAmount / months;
        return new PrepaidCreditStatus(
            MonthsUsed: elapsed,
            MonthsCovered: months,
            PerMonth: perMonth,
            AmountRemaining: perMonth * (months - elapsed),
            Currency: Currency,
            RunsOutOn: StartDate.AddMonths(months));
    }
}

/// <summary>
/// A prepaid credit's position today. Derived on demand and never stored, so it cannot drift out of
/// step with the transactions that carry the real money.
/// </summary>
public record PrepaidCreditStatus(
    int MonthsUsed,
    int MonthsCovered,
    decimal PerMonth,
    decimal AmountRemaining,
    string Currency,
    DateTime RunsOutOn)
{
    public int MonthsRemaining => MonthsCovered - MonthsUsed;

    /// <summary>True once every month has been used up and the next round needs collecting.</summary>
    public bool NeedsCollecting => MonthsRemaining <= 0;

    /// <summary>The last month before it runs out, which is worth a gentle heads-up.</summary>
    public bool IsNearlyOut => MonthsRemaining == 1;

    public string UsageLabel => $"{MonthsUsed} of {MonthsCovered} months used";

    public string RemainingLabel => $"{Currency} {AmountRemaining:N2} remaining";

    /// <summary>0-1, for the proportion bar.</summary>
    public double UsedShare => MonthsCovered == 0 ? 0 : (double)MonthsUsed / MonthsCovered;
}
