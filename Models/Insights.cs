namespace Arctrix.PersonalMoneyTracker.Models;

// Read-only aggregates computed from TransactionRecord rows. Not database tables.
//
// Two kinds of totals are kept apart on purpose:
//  - Current state (balances, net worth) is converted live into today's base currency.
//  - Period totals (income, spending) add up BaseAmount, which is in the base currency that was
//    active when each transaction was recorded. They are grouped and labeled by that currency,
//    never relabeled into today's.

/// <summary>An amount together with the currency it is expressed in.</summary>
public record CurrencyAmount(string Currency, decimal Amount)
{
    public override string ToString() => $"{Currency} {Amount:N2}";
}

/// <summary>Income and expense for one calendar month, in one recorded base currency.</summary>
public record MonthlyFlow(int Year, int Month, string Currency, decimal Income, decimal Expense)
{
    public DateTime MonthStart => new(Year, Month, 1);

    /// <summary>Income minus expenses; negative when the month overspent.</summary>
    public decimal Saved => Income - Expense;

    public bool IsDeficit => Saved < 0;
}

/// <summary>Net worth at the end of a month, in the currency it was requested in.</summary>
public record MonthlyBalance(DateTime MonthStart, decimal Total);

/// <summary>
/// What a year of saving would come to if the recent past repeats: the average net flow of the
/// last few full months, times twelve. The current month is left out because it is still running,
/// and several months are averaged because one bonus or one big repair shouldn't set the figure
/// for a whole year. It is an estimate, and <see cref="Basis"/> says what it rests on.
/// </summary>
public record SavingsOutlook(string Currency, decimal AverageMonthlyNet, int MonthsUsed)
{
    public static SavingsOutlook None(string currency) => new(currency, 0, 0);

    public decimal ProjectedAnnual => AverageMonthlyNet * 12;

    /// <summary>False until a full calendar month has been completed; nothing is projected before that.</summary>
    public bool HasData => MonthsUsed > 0;

    /// <summary>True once the full three months the figure is meant to average are available.</summary>
    public bool IsFullHistory => MonthsUsed >= 3;

    public bool IsShortfall => AverageMonthlyNet < 0;

    public string Basis => MonthsUsed switch
    {
        0 => "Needs a full month of history first",
        1 => "Based on your 1 full month so far - less than the 3 months this normally averages",
        2 => "Based on your 2 full months so far - less than the 3 months this normally averages",
        _ => $"Based on your last {MonthsUsed} full months"
    };
}

/// <summary>Expense total for one category within a period, in one recorded base currency.</summary>
public class CategorySpend
{
    public int CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = Helpers.CategoryIcons.Other;
    public string IconFile => Helpers.CategoryIcons.FileFor(Icon);
    public string ColorHex { get; init; } = "#8F98A7";
    public string Currency { get; init; } = string.Empty;
    public decimal Amount { get; init; }

    /// <summary>Share of spending recorded in the same currency.</summary>
    public double PercentOfTotal { get; init; }

    /// <summary>PercentOfTotal as a 0–1 fraction, for proportion bars.</summary>
    public double Share => PercentOfTotal / 100.0;

    public string AmountLabel => $"{Currency} {Amount:N2}";
}

/// <summary>
/// A period total that may span several recorded base currencies: an amount in the primary
/// currency plus any others, each left in its own currency.
/// </summary>
public sealed class MoneySummary
{
    public MoneySummary(IEnumerable<CurrencyAmount> amounts, string primaryCurrency)
    {
        var byCurrency = amounts
            .GroupBy(a => a.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));

        Primary = new CurrencyAmount(primaryCurrency, byCurrency.GetValueOrDefault(primaryCurrency));
        Others = byCurrency
            .Where(kv => kv.Key != primaryCurrency && kv.Value != 0)
            .OrderBy(kv => kv.Key)
            .Select(kv => new CurrencyAmount(kv.Key, kv.Value))
            .ToList();
    }

    public CurrencyAmount Primary { get; }

    public IReadOnlyList<CurrencyAmount> Others { get; }

    public bool HasOthers => Others.Count > 0;

    /// <summary>"USD 10.00", or "USD 10.00 + MYR 470.00" when other currencies are present.</summary>
    public string Label => string.Join(" + ", Others.Prepend(Primary));

    /// <summary>The non-primary amounts, e.g. "MYR 470.00"; empty when there are none.</summary>
    public string OthersLabel => string.Join(" + ", Others);

    /// <summary>
    /// The currency a period is primarily shown in: today's base currency when the period has
    /// anything recorded in it (or nothing at all), otherwise the most-used recorded currency.
    /// </summary>
    public static string PickPrimary(IEnumerable<string> recordedCurrencies, string currentBase)
    {
        var counts = recordedCurrencies
            .GroupBy(c => c)
            .Select(g => (Currency: g.Key, Count: g.Count()))
            .ToList();

        if (counts.Count == 0 || counts.Any(c => c.Currency == currentBase))
            return currentBase;

        return counts.OrderByDescending(c => c.Count).ThenBy(c => c.Currency).First().Currency;
    }
}

/// <summary>A labelled slice of a total, e.g. balance held in one account type. Share is 0–1.</summary>
public record BalanceShare(string Label, decimal Amount, double Share);

/// <summary>One labelled value on a chart axis.</summary>
public record ChartPoint(string Label, double Value);
