using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

public interface ISettingsService
{
    Task<AppSettings> GetAsync();
    Task SaveAsync(AppSettings settings);
}

public class SettingsService : ISettingsService
{
    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db) => _db = db;

    public async Task<AppSettings> GetAsync()
    {
        await _db.InitializeAsync();
        var settings = await _db.Connection.Table<AppSettings>().FirstOrDefaultAsync();
        return settings ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _db.InitializeAsync();
        await _db.Connection.UpdateAsync(settings);
    }
}
