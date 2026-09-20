using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>
/// Prepaid credits: lump sums collected upfront and counted down month by month.
///
/// Purely informational. Unlike every other service here, this one never writes a TransactionRecord
/// and never touches an account balance - the money was already recorded as ordinary income when it
/// arrived, and counting it again would double it.
/// </summary>
public interface IPrepaidCreditService
{
    Task<List<PrepaidCredit>> GetAllAsync(bool includeClosed = false);
    Task<PrepaidCredit?> GetByIdAsync(int id);
    Task<int> SaveAsync(PrepaidCredit credit);

    /// <summary>Ends an arrangement without deleting its history.</summary>
    Task CloseAsync(int id);

    /// <summary>Credits whose months have all been used and that the user hasn't been reminded about.</summary>
    Task<List<PrepaidCredit>> GetDueForCollectionAsync(DateTime today);

    /// <summary>Records that the reminder has gone out, so it isn't sent again.</summary>
    Task MarkCollectionNotifiedAsync(IEnumerable<PrepaidCredit> credits);
}

public class PrepaidCreditService : IPrepaidCreditService
{
    /// <summary>A credit that stays unpaid is mentioned again after this long, not every run.</summary>
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;

    public PrepaidCreditService(AppDbContext db) => _db = db;

    public async Task<List<PrepaidCredit>> GetAllAsync(bool includeClosed = false)
    {
        await _db.InitializeAsync();
        var all = await _db.Connection.Table<PrepaidCredit>().ToListAsync();
        return all
            .Where(c => includeClosed || !c.IsClosed)
            .OrderBy(c => c.StartDate.AddMonths(Math.Max(1, c.MonthsCovered)))
            .ToList();
    }

    public async Task<PrepaidCredit?> GetByIdAsync(int id)
    {
        await _db.InitializeAsync();
        return await _db.Connection.Table<PrepaidCredit>().FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<int> SaveAsync(PrepaidCredit credit)
    {
        await _db.InitializeAsync();
        if (credit.Id == 0)
            await _db.Connection.InsertAsync(credit);
        else
            await _db.Connection.UpdateAsync(credit);
        return credit.Id;
    }

    public async Task CloseAsync(int id)
    {
        var credit = await GetByIdAsync(id);
        if (credit is null)
            return;

        credit.IsClosed = true;
        await _db.Connection.UpdateAsync(credit);
    }

    public async Task<List<PrepaidCredit>> GetDueForCollectionAsync(DateTime today) =>
        (await GetAllAsync())
        .Where(c => c.StatusOn(today).NeedsCollecting)
        .Where(c => c.CollectionNotifiedAt is not { } told || DateTime.Now - told > ReminderInterval)
        .ToList();

    public async Task MarkCollectionNotifiedAsync(IEnumerable<PrepaidCredit> credits)
    {
        await _db.InitializeAsync();
        foreach (var credit in credits)
        {
            credit.CollectionNotifiedAt = DateTime.Now;
            await _db.Connection.UpdateAsync(credit);
        }
    }
}
