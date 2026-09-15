using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>One day's transactions, for grouped lists.</summary>
public class TransactionGroup : List<TransactionRowViewModel>
{
    public TransactionGroup(DateTime day, IEnumerable<TransactionRowViewModel> rows)
        : base(rows)
    {
        Day = day.Date;

        // Nets are kept per recorded base currency rather than added across currencies.
        NetLabel = string.Join(" · ", this
            .GroupBy(r => r.BaseCurrency)
            .Select(g => (Currency: g.Key,
                Net: g.Where(r => r.Type == TransactionType.Income).Sum(r => r.BaseAmount)
                     - g.Where(r => r.Type == TransactionType.Expense).Sum(r => r.BaseAmount)))
            .Where(x => x.Net != 0)
            .Select(x => $"{(x.Net > 0 ? "+" : "−")}{x.Currency} {Math.Abs(x.Net):N2}"));
    }

    public DateTime Day { get; }

    public string Title => Day == DateTime.Today ? "Today"
        : Day == DateTime.Today.AddDays(-1) ? "Yesterday"
        : Day.ToString("ddd, d MMM yyyy");

    /// <summary>Income minus expenses for the day, per recorded currency; empty when it nets to zero.</summary>
    public string NetLabel { get; }
}
