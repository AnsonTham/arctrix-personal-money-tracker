using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>One day's transactions, for grouped lists.</summary>
public class TransactionGroup : List<TransactionRowViewModel>
{
    public TransactionGroup(DateTime day, IEnumerable<TransactionRowViewModel> rows, string baseCurrency)
        : base(rows)
    {
        Day = day.Date;

        var net = this.Where(r => r.Type == TransactionType.Income).Sum(r => r.BaseAmount)
                  - this.Where(r => r.Type == TransactionType.Expense).Sum(r => r.BaseAmount);
        NetLabel = net == 0 ? string.Empty : $"{(net > 0 ? "+" : "−")}{baseCurrency} {Math.Abs(net):N2}";
    }

    public DateTime Day { get; }

    public string Title => Day == DateTime.Today ? "Today"
        : Day == DateTime.Today.AddDays(-1) ? "Yesterday"
        : Day.ToString("ddd, d MMM yyyy");

    /// <summary>Income minus expenses for the day; empty when the day nets to zero.</summary>
    public string NetLabel { get; }
}
