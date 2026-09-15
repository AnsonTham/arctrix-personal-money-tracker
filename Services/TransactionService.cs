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
    /// Inserts a transaction and applies its effect to the affected account balance(s), atomically.
    /// BaseCurrencyAtEntry must be set to the currency BaseAmount was computed in.
    /// </summary>
    Task AddAsync(TransactionRecord transaction);

    /// <summary>Reverses the stored balance effect, applies the new one, and saves the row, atomically.</summary>
    Task UpdateAsync(TransactionRecord original, TransactionRecord updated);

    /// <summary>Reverses the balance effect and deletes the row, atomically.</summary>
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
        await _db.Connection.RunInTransactionAsync(conn => BalanceLedger.Insert(conn, transaction, _currency));
    }

    public async Task UpdateAsync(TransactionRecord original, TransactionRecord updated)
    {
        RequireRecordedCurrency(updated);
        await _db.InitializeAsync();
        await _db.Connection.RunInTransactionAsync(conn => BalanceLedger.Update(conn, original, updated, _currency));
    }

    public async Task DeleteAsync(TransactionRecord transaction)
    {
        await _db.InitializeAsync();
        await _db.Connection.RunInTransactionAsync(conn => BalanceLedger.Delete(conn, transaction));
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
            .Select(t => (t.Date, Change: BalanceLedger.Effects(t).Sum(e =>
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
}
