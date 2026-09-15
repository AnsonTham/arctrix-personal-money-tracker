using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

public interface IAccountService
{
    Task<List<Account>> GetAllAsync(bool includeArchived = false);
    Task<Account?> GetByIdAsync(int id);
    Task<int> SaveAsync(Account account);
    Task ArchiveAsync(int accountId);
    Task AdjustBalanceAsync(int accountId, decimal delta);

    /// <summary>
    /// The account's balance expressed in <paramref name="currency"/>. Balances are stored in
    /// each account's own currency, so convert before adding accounts together.
    /// </summary>
    decimal BalanceIn(Account account, string currency);
}

public class AccountService : IAccountService
{
    private readonly AppDbContext _db;
    private readonly ICurrencyService _currency;

    public AccountService(AppDbContext db, ICurrencyService currency)
    {
        _db = db;
        _currency = currency;
    }

    public async Task<List<Account>> GetAllAsync(bool includeArchived = false)
    {
        await _db.InitializeAsync();
        var query = _db.Connection.Table<Account>();
        var all = await query.ToListAsync();
        return includeArchived ? all : all.Where(a => !a.IsArchived).ToList();
    }

    public async Task<Account?> GetByIdAsync(int id)
    {
        await _db.InitializeAsync();
        return await _db.Connection.Table<Account>().FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<int> SaveAsync(Account account)
    {
        await _db.InitializeAsync();
        if (account.Id == 0)
        {
            await _db.Connection.InsertAsync(account);
        }
        else
        {
            await _db.Connection.UpdateAsync(account);
        }
        return account.Id;
    }

    public async Task ArchiveAsync(int accountId)
    {
        var account = await GetByIdAsync(accountId);
        if (account is null) return;
        account.IsArchived = true;
        await _db.Connection.UpdateAsync(account);
    }

    public async Task AdjustBalanceAsync(int accountId, decimal delta)
    {
        var account = await GetByIdAsync(accountId);
        if (account is null) return;
        account.Balance += delta;
        await _db.Connection.UpdateAsync(account);
    }

    public decimal BalanceIn(Account account, string currency)
        => _currency.Convert(account.Balance, account.Currency, currency);
}
