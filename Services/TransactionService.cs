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
}

public class TransactionService : ITransactionService
{
    private readonly AppDbContext _db;
    private readonly IAccountService _accounts;

    public TransactionService(AppDbContext db, IAccountService accounts)
    {
        _db = db;
        _accounts = accounts;
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
        await _db.Connection.InsertAsync(transaction);
        await ApplyBalanceEffectAsync(transaction, reverse: false);
    }

    public async Task UpdateAsync(TransactionRecord original, TransactionRecord updated)
    {
        await _db.InitializeAsync();
        await ApplyBalanceEffectAsync(original, reverse: true);
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

    /// <summary>
    /// Applies (or reverses, when reverse=true) a transaction's effect on account
    /// balances. Amounts are applied in the account's own currency using the
    /// transaction's OriginalAmount, since Account.Balance is stored per-account.
    /// </summary>
    private async Task ApplyBalanceEffectAsync(TransactionRecord t, bool reverse)
    {
        var sign = reverse ? -1 : 1;

        switch (t.Type)
        {
            case TransactionType.Income:
                await _accounts.AdjustBalanceAsync(t.AccountId, sign * t.OriginalAmount);
                break;

            case TransactionType.Expense:
                await _accounts.AdjustBalanceAsync(t.AccountId, -sign * t.OriginalAmount);
                break;

            case TransactionType.Transfer:
            case TransactionType.Investment:
                await _accounts.AdjustBalanceAsync(t.AccountId, -sign * t.OriginalAmount);
                if (t.ToAccountId is int toId)
                {
                    await _accounts.AdjustBalanceAsync(toId, sign * t.OriginalAmount);
                }
                break;
        }
    }
}
