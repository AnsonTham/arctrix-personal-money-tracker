using System.Globalization;
using System.Text.RegularExpressions;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services.Telegram;

/// <summary>What a chat message asked for, when it reads as a transaction.</summary>
public sealed record ParsedEntry(TransactionType Type, decimal Amount, string Description, DateTime Date);

/// <summary>
/// Reads a transaction out of a plain chat message - "spent 12.50 on lunch", "rm18 grab",
/// "salary 3200 income". Deliberately forgiving: whatever it gets wrong, the user sees in the
/// confirmation message and can correct before anything is saved.
/// </summary>
public static partial class EntryParser
{
    private static readonly string[] IncomeWords =
        ["income", "earned", "received", "salary", "refund", "bonus", "paid me", "got"];

    private static readonly string[] ExpenseWords =
        ["spent", "spend", "paid", "bought", "buy", "expense", "cost"];

    /// <summary>Returns null when there is no amount in the message, which is how "not a transaction" is signalled.</summary>
    public static ParsedEntry? TryParse(string? text, DateTime today)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = AmountPattern().Match(text);
        if (!match.Success)
            return null;

        if (!decimal.TryParse(match.Groups["amount"].Value.Replace(",", string.Empty),
                NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return null;

        var lower = text.ToLowerInvariant();
        var type = IncomeWords.Any(w => lower.Contains(w, StringComparison.Ordinal)) && !ExpenseWords.Any(w => lower.Contains(w, StringComparison.Ordinal))
            ? TransactionType.Income
            : TransactionType.Expense;

        var date = today;
        if (lower.Contains("yesterday", StringComparison.Ordinal))
            date = today.AddDays(-1);

        return new ParsedEntry(type, amount, Describe(text, match), date);
    }

    /// <summary>Everything except the amount and the words that only said what kind of entry it is.</summary>
    private static string Describe(string text, Match amount)
    {
        var withoutAmount = text.Remove(amount.Index, amount.Length);
        var words = withoutAmount
            .Split([' ', '\t', '\n', '\r', ',', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => !IsNoise(w))
            .ToList();

        var description = string.Join(' ', words).Trim();
        return description.Length == 0
            ? string.Empty
            : char.ToUpperInvariant(description[0]) + description[1..];
    }

    /// <summary>
    /// Words that only said what kind of entry this is. Words like "salary" or "refund" are left in:
    /// they hint at the type and make a good description.
    /// </summary>
    private static bool IsNoise(string word)
    {
        var lower = word.ToLowerInvariant().Trim('-', ':', ';');
        return lower is "on" or "for" or "at" or "the" or "a" or "an" or "of" or "to" or "add" or "today" or "yesterday" or "rm" or "myr"
            or "spent" or "spend" or "paid" or "bought" or "buy" or "expense" or "cost" or "income" or "earned" or "received" or "got";
    }

    /// <summary>
    /// The first money-looking number: an optional currency prefix, then either a grouped figure
    /// ("1,250.00") or a plain one. Neither may run into more digits, so "3200" isn't read as "320".
    /// </summary>
    [GeneratedRegex(@"(?i)(?:rm|myr|\$)?\s*(?<amount>\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)(?!\d)")]
    private static partial Regex AmountPattern();
}
