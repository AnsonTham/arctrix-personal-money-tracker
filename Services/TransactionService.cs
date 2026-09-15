using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

public interface ITransactionService
{
    Task<List<TransactionRecord>> GetAllAsync();
    Task<List<TransactionRecord>> GetRecentAsync(int count);
    Task<List<TransactionRecord>> GetForMonthAsync(int year, int month);
    Task<TransactionRecord?> GetByIdAsync(int id);

    /// <summary>Inserts a transaction and applies its effect to the affected account balance(s).</summary>
    Task AddAsync(TransactionRecord transaction);

    /// <summary>Reverses the old balance effect, applies the new one, and saves the edited row.</summary>
    Task UpdateAsync(TransactionRecord original, TransactionRecord updated);

    /// <summary>Reverses the balance effect and deletes the row.</summary>
    Task DeleteAsync(TransactionRecord transaction);

    Task<decimal> GetMonthlyTotalAsync(TransactionType type, int year, int month);

    /// <summary>Per-month income, expense and net change for the <paramref name="monthCount"/> months ending with <paramref name="endMonth"/>'s month, oldest first.</summary>
    Task<IReadOnlyList<MonthlyFlow>> GetMonthlyFlowsAsync(DateTime endMonth, int monthCount);

    /// <summary>Expense totals grouped by category for one month, largest first.</summary>
    Task<IReadOnlyList<CategorySpend>> GetCategorySpendAsync(int year, int month);
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
        await _db.InitializeAsync();
        await SetAccountAmountsAsync(transaction);
        await _db.Connection.InsertAsync(transaction);
        await ApplyBalanceEffectAsync(transaction, reverse: false);
    }

    public async Task UpdateAsync(TransactionRecord original, TransactionRecord updated)
    {
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

    public async Task<decimal> GetMonthlyTotalAsync(TransactionType type, int year, int month)
    {
        var monthly = await GetForMonthAsync(year, month);
        return monthly.Where(t => t.Type == type).Sum(t => t.BaseAmount);
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

        return Enumerable.Range(0, monthCount)
            .Select(i => firstMonth.AddMonths(i))
            .Select(start =>
            {
                var month = inRange.Where(t => t.Date.Year == start.Year && t.Date.Month == start.Month).ToList();
                var income = month.Where(t => t.Type == TransactionType.Income).Sum(t => t.BaseAmount);
                var expense = month.Where(t => t.Type == TransactionType.Expense).Sum(t => t.BaseAmount);

                // Transfers/investments between tracked accounts leave the total unchanged;
                // one without a destination account moves money out of the tracked total.
                var leftTracked = month
                    .Where(t => t.Type is TransactionType.Transfer or TransactionType.Investment && t.ToAccountId is null)
                    .Sum(t => t.BaseAmount);

                return new MonthlyFlow(start.Year, start.Month, income, expense, income - expense - leftTracked);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<CategorySpend>> GetCategorySpendAsync(int year, int month)
    {
        var expenses = (await GetForMonthAsync(year, month))
            .Where(t => t.Type == TransactionType.Expense)
            .ToList();
        var total = expenses.Sum(t => t.BaseAmount);
        var categories = (await _categories.GetAllAsync(includeArchived: true)).ToDictionary(c => c.Id);

        return expenses
            .GroupBy(t => t.CategoryId)
            .Select(g =>
            {
                categories.TryGetValue(g.Key, out var category);
                var amount = g.Sum(t => t.BaseAmount);
                return new CategorySpend
                {
                    CategoryId = g.Key,
                    Name = category?.Name ?? "Others",
                    Icon = category?.Icon ?? "•",
                    ColorHex = category?.ColorHex ?? "#8F98A7",
                    Amount = amount,
                    PercentOfTotal = total == 0 ? 0 : (double)(amount / total) * 100.0
                };
            })
            .OrderByDescending(c => c.Amount)
            .ToList();
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
    /// Applies (or reverses, when reverse=true) a transaction's effect on account balances using
    /// the per-account amounts from <see cref="SetAccountAmountsAsync"/>. Rows saved before those
    /// columns existed were applied with OriginalAmount, so they also reverse with it.
    /// </summary>
    private async Task ApplyBalanceEffectAsync(TransactionRecord t, bool reverse)
    {
        var sign = reverse ? -1 : 1;
        var fromAmount = t.AccountAmount ?? t.OriginalAmount;

        switch (t.Type)
        {
            case TransactionType.Income:
                await _accounts.AdjustBalanceAsync(t.AccountId, sign * fromAmount);
                break;

            case TransactionType.Expense:
                await _accounts.AdjustBalanceAsync(t.AccountId, -sign * fromAmount);
                break;

            case TransactionType.Transfer:
            case TransactionType.Investment:
                await _accounts.AdjustBalanceAsync(t.AccountId, -sign * fromAmount);
                if (t.ToAccountId is int toId)
                {
                    await _accounts.AdjustBalanceAsync(toId, sign * (t.ToAccountAmount ?? t.OriginalAmount));
                }
                break;
        }
    }
}
