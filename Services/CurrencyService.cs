namespace Arctrix.PersonalMoneyTracker.Services;

public interface ICurrencyService
{
    IReadOnlyList<string> SupportedCurrencies { get; }

    /// <summary>Rate to convert 1 unit of <paramref name="from"/> into <paramref name="to"/>.</summary>
    decimal GetRate(string from, string to);

    decimal Convert(decimal amount, string from, string to);
}

/// <summary>
/// Manual exchange-rate table. No external API calls are made here on purpose -
/// live FX lookup is future-architecture (see project brief) and would need a
/// provider key managed via secure configuration, not hardcoded. Until that
/// lands, rates are editable in Settings and stored as plain multipliers
/// against MYR.
/// </summary>
public class CurrencyService : ICurrencyService
{
    private readonly Dictionary<string, decimal> _ratesToMyr = new()
    {
        ["MYR"] = 1.00m,
        ["USD"] = 4.70m,
        ["SGD"] = 3.48m,
        ["EUR"] = 5.10m,
        ["GBP"] = 5.95m,
        ["JPY"] = 0.031m,
    };

    public IReadOnlyList<string> SupportedCurrencies => _ratesToMyr.Keys.ToList();

    public decimal GetRate(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return 1.0m;

        var fromRate = _ratesToMyr.GetValueOrDefault(from.ToUpperInvariant(), 1.0m);
        var toRate = _ratesToMyr.GetValueOrDefault(to.ToUpperInvariant(), 1.0m);
        return fromRate / toRate;
    }

    public decimal Convert(decimal amount, string from, string to) => amount * GetRate(from, to);
}
