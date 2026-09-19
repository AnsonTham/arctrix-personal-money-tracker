using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>Read-only projection of a RecurringPayment for list display.</summary>
public class RecurringRowViewModel
{
    public required RecurringPayment Payment { get; init; }
    public string CategoryName { get; init; } = "Others";
    public string CategoryIcon { get; init; } = CategoryIcons.Other;
    public string AccountName { get; init; } = string.Empty;

    /// <summary>The occurrence waiting on funds, when there is one; it is retried on every run.</summary>
    public RecurringSkip? Skip { get; init; }

    public bool HasSkip => Skip is not null;

    public string SkipLabel => Skip is null
        ? string.Empty
        : $"Skipped {Skip.DueDate:d MMM} - not enough in {AccountName} "
          + $"({Skip.Currency} {Skip.Balance:N2} of {Skip.Currency} {Skip.Amount:N2}). Retries when topped up.";

    public string CategoryIconFile => CategoryIcons.FileFor(CategoryIcon);

    public string Name => Payment.Name;

    public bool IsIncome => Payment.Type == TransactionType.Income;

    public string AmountLabel => $"{(IsIncome ? "+ " : "- ")}{Payment.Currency} {Payment.Amount:N2}";

    public string ScheduleLabel => $"{CategoryName} · {AccountName} · {Ordinal(Payment.DayOfMonth)} of each month";

    public bool IsOverdue => Payment.NextDueDate.Date < DateTime.Today;

    public string DueLabel
    {
        get
        {
            var days = (Payment.NextDueDate.Date - DateTime.Today).Days;
            return days switch
            {
                < 0 => "Overdue",
                0 => "Due today",
                1 => "Due tomorrow",
                < 14 => $"Due in {days} days",
                _ => $"Due {Payment.NextDueDate:d MMM}"
            };
        }
    }

    private static string Ordinal(int day) => day switch
    {
        1 or 21 or 31 => $"{day}st",
        2 or 22 => $"{day}nd",
        3 or 23 => $"{day}rd",
        _ => $"{day}th"
    };
}
