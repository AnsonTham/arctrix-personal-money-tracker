using SQLite;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Data;

/// <summary>
/// Owns the single SQLite connection for the app and is responsible for
/// creating/upgrading the schema. CreateTableAsync&lt;T&gt; is additive-only in
/// sqlite-net (it adds missing columns, never drops data), so calling it on
/// every launch is a safe way to evolve the schema without destroying rows.
/// </summary>
public class AppDbContext
{
    private const string DbFileName = "arctrix.db3";
    private SQLiteAsyncConnection? _connection;
    private Task? _initialization;

    public SQLiteAsyncConnection Connection
        => _connection ?? throw new InvalidOperationException("Call InitializeAsync() before using the database.");

    public Task InitializeAsync()
    {
        // Guard against concurrent callers each kicking off their own init.
        return _initialization ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, DbFileName);
        _connection = new SQLiteAsyncConnection(dbPath);

        await _connection.CreateTableAsync<Account>();
        await _connection.CreateTableAsync<Category>();
        await _connection.CreateTableAsync<TransactionRecord>();
        await _connection.CreateTableAsync<RecurringPayment>();
        await _connection.CreateTableAsync<RecurringSkip>();
        await _connection.CreateTableAsync<PrepaidCredit>();
        await _connection.CreateTableAsync<PublicHoliday>();
        await _connection.CreateTableAsync<AppSettings>();

        await SeedIfEmptyAsync();
        await StampRecordedBaseCurrencyAsync();
        await StampRecurrenceRulesAsync();
        await UpdateSeededCategoryIconsAsync();
    }

    /// <summary>
    /// Seeded categories originally stored emoji as their icon. Point them at the line-icon keys;
    /// only the icon column of the built-in categories changes, and it's a no-op once updated.
    /// </summary>
    private async Task UpdateSeededCategoryIconsAsync()
    {
        foreach (var (name, icon, _) in Category.QuickAddDefaults)
        {
            await Connection.ExecuteAsync(
                $"UPDATE {nameof(Category)} SET {nameof(Category.Icon)} = ? " +
                $"WHERE {nameof(Category.IsSystem)} = 1 AND {nameof(Category.Name)} = ? AND {nameof(Category.Icon)} <> ?",
                icon, name, icon);
        }
    }

    /// <summary>
    /// Recurring payments saved before the weekday rule existed have null in its columns, which is
    /// read back as the fixed-day rule they already used. Writing the values down makes that
    /// explicit rather than relying on how a null maps. A no-op once stamped.
    /// </summary>
    private async Task StampRecurrenceRulesAsync()
    {
        await Connection.ExecuteAsync(
            $"UPDATE {nameof(RecurringPayment)} SET " +
            $"{nameof(RecurringPayment.RuleType)} = ?, " +
            $"{nameof(RecurringPayment.Weekday)} = ?, " +
            $"{nameof(RecurringPayment.Occurrence)} = ? " +
            $"WHERE {nameof(RecurringPayment.RuleType)} IS NULL",
            (int)RecurrenceRuleType.FixedDayOfMonth,
            (int)DayOfWeek.Friday,
            (int)MonthlyOccurrence.Last);
    }

    /// <summary>
    /// Transactions saved before TransactionRecord.BaseCurrencyAtEntry existed have no record of
    /// which base currency their BaseAmount was computed in. The base currency setting at upgrade
    /// time is the best record available, so they are stamped with it. A no-op once stamped.
    /// </summary>
    private async Task StampRecordedBaseCurrencyAsync()
    {
        var settings = await Connection.Table<AppSettings>().FirstOrDefaultAsync();
        await Connection.ExecuteAsync(
            $"UPDATE {nameof(TransactionRecord)} SET {nameof(TransactionRecord.BaseCurrencyAtEntry)} = ? " +
            $"WHERE {nameof(TransactionRecord.BaseCurrencyAtEntry)} IS NULL",
            settings?.BaseCurrency ?? "MYR");
    }

    private async Task SeedIfEmptyAsync()
    {
        if (await Connection.Table<AppSettings>().CountAsync() == 0)
        {
            await Connection.InsertAsync(new AppSettings());
        }

        if (await Connection.Table<Category>().CountAsync() == 0)
        {
            var defaults = Category.QuickAddDefaults.Select(d => new Category
            {
                Name = d.Name,
                Icon = d.Icon,
                DefaultType = d.Type,
                IsSystem = true,
                ColorHex = d.Type switch
                {
                    TransactionType.Income => "#22D3A2",
                    TransactionType.Investment => "#5EEAD4",
                    TransactionType.Transfer => "#8F98A7",
                    _ => "#FF6B83"
                }
            });
            await Connection.InsertAllAsync(defaults);
        }

        // Malaysia's national holidays as a starting point; the user edits the list from Settings.
        // Seeded only when empty, so removing one doesn't bring it back on the next launch.
        if (await Connection.Table<PublicHoliday>().CountAsync() == 0)
        {
            await Connection.InsertAllAsync(PublicHoliday.MalaysiaDefaults.Select(h => new PublicHoliday
            {
                Date = h.Date,
                Name = h.Name
            }));
        }

        if (await Connection.Table<Account>().CountAsync() == 0)
        {
            await Connection.InsertAllAsync(new[]
            {
                new Account { Name = "Main Bank", Type = AccountType.Bank, Currency = "MYR", Balance = 5200m, ColorHex = "#5EC8FF" },
                new Account { Name = "Cash",      Type = AccountType.Cash, Currency = "MYR", Balance = 680m,  ColorHex = "#22D3A2" },
                new Account { Name = "E-Wallet",  Type = AccountType.EWallet, Currency = "MYR", Balance = 420m, ColorHex = "#FFC55E" },
            });
        }
    }
}
