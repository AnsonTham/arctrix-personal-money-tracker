namespace Arctrix.PersonalMoneyTracker.Models;

// Read-only aggregates computed from TransactionRecord rows. Not database tables.

/// <summary>Income, expense and net change of the tracked total for one calendar month, in base currency.</summary>
public record MonthlyFlow(int Year, int Month, decimal Income, decimal Expense, decimal NetChange)
{
    public DateTime MonthStart => new(Year, Month, 1);
}

/// <summary>Expense total for one category within a period, in base currency.</summary>
public class CategorySpend
{
    public int CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = "•";
    public string ColorHex { get; init; } = "#8F98A7";
    public decimal Amount { get; init; }
    public double PercentOfTotal { get; init; }

    /// <summary>PercentOfTotal as a 0–1 fraction, for proportion bars.</summary>
    public double Share => PercentOfTotal / 100.0;
}

/// <summary>A labelled slice of a total, e.g. balance held in one account type. Share is 0–1.</summary>
public record BalanceShare(string Label, decimal Amount, double Share);

/// <summary>One labelled value on a chart axis.</summary>
public record ChartPoint(string Label, double Value);
