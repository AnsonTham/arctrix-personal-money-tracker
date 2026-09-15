using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>Read-only projection of a TransactionRecord for list display.</summary>
public class TransactionRowViewModel
{
    public int Id { get; init; }
    public TransactionType Type { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string CategoryIcon { get; init; } = CategoryIcons.Other;
    public string AccountName { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public decimal BaseAmount { get; init; }
    public string Notes { get; init; } = string.Empty;
    public string BaseCurrency { get; init; } = "MYR";

    public string CategoryIconFile => CategoryIcons.FileFor(CategoryIcon);

    public string DateLabel => Date.Date == DateTime.Today ? "Today"
        : Date.Date == DateTime.Today.AddDays(-1) ? "Yesterday"
        : Date.ToString("d MMM");

    public string Subtitle => string.IsNullOrWhiteSpace(Notes) ? AccountName : $"{AccountName} · {Notes}";

    public bool IsPositive => Type == TransactionType.Income;

    public string AmountLabel =>
        (Type == TransactionType.Income ? "+ " : Type == TransactionType.Expense ? "- " : "") +
        $"{BaseCurrency} {BaseAmount:N2}";

    public static TransactionRowViewModel From(TransactionRecord record, Category? category, Account? account, string baseCurrency) => new()
    {
        Id = record.Id,
        Type = record.Type,
        CategoryName = category?.Name ?? "Others",
        CategoryIcon = category?.Icon ?? CategoryIcons.Other,
        AccountName = account?.Name ?? string.Empty,
        Date = record.Date,
        BaseAmount = record.BaseAmount,
        Notes = record.Notes,
        BaseCurrency = record.BaseCurrencyAtEntry ?? baseCurrency
    };

    public bool Matches(string query) =>
        CategoryName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || AccountName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Notes.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
