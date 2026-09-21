namespace Arctrix.PersonalMoneyTracker.Models;

public enum AccountType
{
    Bank,
    Cash,
    EWallet,
    Investment
}

public enum TransactionType
{
    Income,
    Expense,
    Transfer,
    Investment
}

public enum RecurrenceFrequency
{
    Monthly
}

/// <summary>How a monthly recurrence picks its date.</summary>
public enum RecurrenceRuleType
{
    /// <summary>The same day number every month, clamped to shorter months. The original behaviour.</summary>
    FixedDayOfMonth,

    /// <summary>A weekday position, such as the last Friday - a date that moves from month to month.</summary>
    NthWeekdayOfMonth,

    /// <summary>
    /// The last day of the month that is actually worked: weekends and the listed public holidays
    /// are stepped back over. Added last, so the numbers already stored keep their meaning.
    /// </summary>
    LastWorkingDayOfMonth
}

/// <summary>Which occurrence of a weekday within a month.</summary>
public enum MonthlyOccurrence
{
    First,
    Second,
    Third,
    Fourth,

    /// <summary>The last one in the month, which is the fourth or the fifth depending on the month.</summary>
    Last
}
