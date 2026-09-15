using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

public interface ITransactionService
{
    Task<List<TransactionRecord>> GetAllAsync();
    Task<List<TransactionRecord>> GetRecentAsync(int count);
    Task<List<TransactionRecord>> GetForMonthAsync(int year, int month);
    Task<TransactionRecord?> GetByIdAsync(int id);

    /// <summary>
    /// Inserts a transaction and applies its effect to the affected account balance(s).
    /// BaseCurrencyAtEntry must be set to the currency BaseAmount was computed in.
    /// </summary>
    Task AddAsync(TransactionRecord transaction);

    /// <summary>Reverses the old balance effect, applies the new one, and saves the edited row.</summary>
    Task UpdateAsync(TransactionRecord original, TransactionRecord updated);

    /// <summary>Reverses the balance effect and deletes the row.</summary>
    Task DeleteAsync(TransactionRecord transaction);

    /// <summary>
    /// Income and expense per month and recorded base currency for the <paramref name="monthCount"/>
    /// months ending with <paramref name="endMonth"/>'s month. Months with no income or expense have
    /// no entry. Ordered by month, then currency.
    /// </summary>
    Task<IReadOnlyList<MonthlyFlow>> GetMonthlyFlowsAsync(DateTime endMonth, int monthCount);

    /// <summary>Expense totals per category and recorded base currency for one month, largest first.</summary>
    Task<IReadOnlyList<CategorySpend>> GetCategorySpendAsync(int year, int month);

    /// <summary>
    /// Net worth of active accounts at the end of each of the <paramref name="monthCount"/> months
    /// ending with <paramref name="endMonth"/>'s month, oldest first. Worked back from today's
    /// balances through each transaction's per-account balance changes, all converted live into
    /// <paramref name="currency"/>, so the series matches the live net worth figure.
    /// </summary>
    Task<IReadOnlyList<MonthlyBalance>> GetMonthEndNetWorthAsync(DateTime endMonth, int monthCount, string currency);
}

public class TransactionService : ITransactionService
{
    private readonly AppDbContext _db;
    private readonly IAccountService _accounts;
    private readonly ICategoryService _categories;
    private readonly ICurrencyService _currency;

    public TransactionService(AppDbContext db, IAccountService accounts, ICategoryService categories, ICurrencyService currency)
    {
        _db = db;
        _accounts = accounts;
        _categories = categories;
        _currency = currency;
    }

    public async Task<List<TransactionRecord>> GetAllAsync()
    {
        await _db.InitializeAsync();
        return await _db.Connection.Table<TransactionRecord>()
            .OrderByDescending(t => t.Date)
            .ToListAsync();
    }

    public async Task<List<TransactionRecord>> GetRecentAsync(int count)
    {
        var all = await GetAllAsync();
        return all.Take(count).ToList();
    }

    public async Task<List<TransactionRecord>> GetForMonthAsync(int year, int month)
    {
        var all = await GetAllAsync();
        return all.Where(t => t.Date.Year == year && t.Date.Month == month).ToList();
    }

    public async Task<TransactionRecord?> GetByIdAsync(int id)
    {
        await _db.InitializeAsync();
        return await _db.Connection.Table<TransactionRecord>().FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task AddAsync(TransactionRecord transaction)
    {
        RequireRecordedCurrency(transaction);
        await _db.InitializeAsync();
        await SetAccountAmountsAsync(transaction);
        await _db.Connection.InsertAsync(transaction);
        await ApplyBalanceEffectAsync(transaction, reverse: false);
    }

    public async Task UpdateAsync(TransactionRecord original, TransactionRecord updated)
    {
        RequireRecordedCurrency(updated);
        await _db.InitializeAsync();
        await ApplyBalanceEffectAsync(original, reverse: true);
        await SetAccountAmountsAsync(updated);
        await ApplyBalanceEffectAsync(updated, reverse: false);
        await _db.Connection.UpdateAsync(updated);
    }

    public async Task DeleteAsync(TransactionRecord transaction)
    {
        await _db.InitializeAsync();
        await ApplyBalanceEffectAsync(transaction, reverse: true);
        await _db.Connection.DeleteAsync(transaction);
    }

    public async Task<IReadOnlyList<MonthlyFlow>> GetMonthlyFlowsAsync(DateTime endMonth, int monthCount)
    {
        await _db.InitializeAsync();
        var lastMonth = new DateTime(endMonth.Year, endMonth.Month, 1);
        var firstMonth = lastMonth.AddMonths(1 - monthCount);
        var afterLastMonth = lastMonth.AddMonths(1);
        var inRange = await _db.Connection.Table<TransactionRecord>()
            .Where(t => t.Date >= firstMonth && t.Date < afterLastMonth)
            .ToListAsync();

        return inRange
            .Where(t => t.Type is TransactionType.Income or TransactionType.Expense)
            .GroupBy(t => (t.Date.Year, t.Date.Month, Currency: RecordedCurrency(t)))
            .Select(g => new MonthlyFlow(
                g.Key.Year,
                g.Key.Month,
                g.Key.Currency,
                g.Where(t => t.Type == TransactionType.Income).Sum(t => t.BaseAmount),
                g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.BaseAmount)))
            .OrderBy(f => f.MonthStart)
            .ThenBy(f => f.Currency)
            .ToList();
    }

    public async Task<IReadOnlyList<CategorySpend>> GetCategorySpendAsync(int year, int month)
    {
        var expenses = (await GetForMonthAsync(year, month))
            .Where(t => t.Type == TransactionType.Expense)
            .ToList();
        var totals = expenses
            .GroupBy(RecordedCurrency)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.BaseAmount));
        var categories = (await _categories.GetAllAsync(includeArchived: true)).ToDictionary(c => c.Id);

        return expenses
            .GroupBy(t => (t.CategoryId, Currency: RecordedCurrency(t)))
            .Select(g =>
            {
                categories.TryGetValue(g.Key.CategoryId, out var category);
                var amount = g.Sum(t => t.BaseAmount);
                var total = totals[g.Key.Currency];
                return new CategorySpend
                {
                    CategoryId = g.Key.CategoryId,
                    Name = category?.Name ?? "Others",
                    Icon = category?.Icon ?? "•",
                    ColorHex = category?.ColorHex ?? "#8F98A7",
                    Currency = g.Key.Currency,
                    Amount = amount,
                    PercentOfTotal = total == 0 ? 0 : (double)(amount / total) * 100.0
                };
            })
            .OrderByDescending(c => c.Amount)
            .ToList();
    }

    public async Task<IReadOnlyList<MonthlyBalance>> GetMonthEndNetWorthAsync(DateTime endMonth, int monthCount, string currency)
    {
        await _db.InitializeAsync();
        var accounts = (await _accounts.GetAllAsync(includeArchived: true)).ToDictionary(a => a.Id);
        var total = accounts.Values.Where(a => !a.IsArchived).Sum(a => _accounts.BalanceIn(a, currency));

        var firstMonth = new DateTime(endMonth.Year, endMonth.Month, 1).AddMonths(1 - monthCount);
        var afterFirstMonth = firstMonth.AddMonths(1);
        var later = await _db.Connection.Table<TransactionRecord>()
            .Where(t => t.Date >= afterFirstMonth)
            .ToListAsync();

        // Each transaction's change to active-account balances, converted live like the balances.
        var changes = later
            .Select(t => (t.Date, Change: BalanceEffects(t).Sum(e =>
                accounts.TryGetValue(e.AccountId, out var account) && !account.IsArchived
                    ? _currency.Convert(e.Delta, account.Currency, currency)
                    : 0m)))
            .ToList();

        return Enumerable.Range(0, monthCount)
            .Select(i =>
            {
                var monthStart = firstMonth.AddMonths(i);
                var nextMonth = monthStart.AddMonths(1);
                var closing = total - changes.Where(c => c.Date >= nextMonth).Sum(c => c.Change);
                return new MonthlyBalance(monthStart, closing);
            })
            .ToList();
    }

    private static string RecordedCurrency(TransactionRecord t) => t.BaseCurrencyAtEntry ?? string.Empty;

    private static void RequireRecordedCurrency(TransactionRecord t)
    {
        if (string.IsNullOrWhiteSpace(t.BaseCurrencyAtEntry))
            throw new ArgumentException("Set BaseCurrencyAtEntry to the base currency BaseAmount was computed in.", nameof(t));
    }

    /// <summary>
    /// Records how far each affected account moves, in that account's own currency: a USD 100
    /// expense on a MYR account moves the balance by MYR 470, not 100. Stored on the row so the
    /// effect reverses exactly later.
    /// </summary>
    private async Task SetAccountAmountsAsync(TransactionRecord t)
    {
        t.AccountAmount = await InAccountCurrencyAsync(t, t.AccountId);
        t.ToAccountAmount = t.ToAccountId is int toId ? await InAccountCurrencyAsync(t, toId) : null;
    }

    private async Task<decimal> InAccountCurrencyAsync(TransactionRecord t, int accountId)
    {
        var account = await _accounts.GetByIdAsync(accountId);
        if (account is null)
            return t.OriginalAmount;

        var converted = _currency.Convert(t.OriginalAmount, t.OriginalCurrency, account.Currency);
        return Math.Round(converted, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A transaction's change to each affected account's balance, in that account's currency.
    /// Uses the per-account amounts from <see cref="SetAccountAmountsAsync"/>; rows saved before
    /// those columns existed were applied with OriginalAmount, so they fall back to it.
    /// </summary>
    private static IEnumerable<(int AccountId, decimal Delta)> BalanceEffects(TransactionRecord t)
    {
        var fromAmount = t.AccountAmount ?? t.OriginalAmount;

        switch (t.Type)
        {
            case TransactionType.Income:
                yield return (t.AccountId, fromAmount);
                break;

            case TransactionType.Expense:
                yield return (t.AccountId, -fromAmount);
                break;

            case TransactionType.Transfer:
            case TransactionType.Investment:
                yield return (t.AccountId, -fromAmount);
                if (t.ToAccountId is int toId)
                    yield return (toId, t.ToAccountAmount ?? t.OriginalAmount);
                break;
        }
    }

    /// <summary>Applies (or reverses, when reverse=true) a transaction's effect on account balances.</summary>
    private async Task ApplyBalanceEffectAsync(TransactionRecord t, bool reverse)
    {
        var sign = reverse ? -1 : 1;
        foreach (var (accountId, delta) in BalanceEffects(t))
            await _accounts.AdjustBalanceAsync(accountId, sign * delta);
    }
}
