using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>
/// The user's list of non-working days. Seeded once with Malaysia's national holidays and edited
/// freely after that - nothing here reaches the network, and no date is computed behind the user's
/// back.
/// </summary>
public interface IPublicHolidayService
{
    Task<List<PublicHoliday>> GetAllAsync();

    /// <summary>Just the dates, for the recurrence arithmetic.</summary>
    Task<IReadOnlySet<DateTime>> GetDatesAsync();

    Task<int> SaveAsync(PublicHoliday holiday);
    Task DeleteAsync(int id);
}

public class PublicHolidayService : IPublicHolidayService
{
    private readonly AppDbContext _db;

    public PublicHolidayService(AppDbContext db) => _db = db;

    public async Task<List<PublicHoliday>> GetAllAsync()
    {
        await _db.InitializeAsync();
        var all = await _db.Connection.Table<PublicHoliday>().ToListAsync();
        return all.OrderBy(h => h.Date).ToList();
    }

    public async Task<IReadOnlySet<DateTime>> GetDatesAsync() =>
        (await GetAllAsync()).Select(h => h.Date.Date).ToHashSet();

    public async Task<int> SaveAsync(PublicHoliday holiday)
    {
        await _db.InitializeAsync();
        holiday.Date = holiday.Date.Date;
        if (holiday.Id == 0)
            await _db.Connection.InsertAsync(holiday);
        else
            await _db.Connection.UpdateAsync(holiday);
        return holiday.Id;
    }

    public async Task DeleteAsync(int id)
    {
        await _db.InitializeAsync();
        await _db.Connection.DeleteAsync<PublicHoliday>(id);
    }
}
