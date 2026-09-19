using SQLite;
using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

public interface IRecurringPaymentService
{
    Task<List<RecurringPayment>> GetAllAsync(bool includeInactive = false);
    Task<int> SaveAsync(RecurringPayment payment);
    Task DeactivateAsync(int id);

    /// <summary>
    /// Posts a TransactionRecord for every occurrence that has come due - including months
    /// missed while the app wasn't opened - and advances NextDueDate past today. Safe to call
    /// repeatedly and concurrently: runs are serialized, each occurrence is posted and its
    /// schedule advanced in one database transaction, and an occurrence that already has a
    /// posted transaction is never posted again.
    ///
    /// An occurrence the paying account can't cover is not posted and not skipped over: it is
    /// recorded (see <see cref="RecurringSkip"/>), the schedule stays where it is, and the next
    /// run tries the same occurrence again - so topping the account up is all it takes.
    /// </summary>
    Task<RecurringRunResult> RunDuePaymentsAsync();

    /// <summary>Occurrences waiting on funds, newest first. Resolved ones are left out.</summary>
    Task<List<RecurringSkip>> GetUnresolvedSkipsAsync();

    /// <summary>Records that the user has been told about these shortfalls, so they aren't repeated.</summary>
    Task MarkSkipsNotifiedAsync(IEnumerable<RecurringSkip> skips);
}

/// <summary>What one run of the due payments did.</summary>
/// <param name="Posted">Transactions written.</param>
/// <param name="Skipped">Occurrences left unpaid for lack of funds, including ones skipped before.</param>
/// <param name="Resolved">Occurrences that had been skipped and posted this time.</param>
public sealed record RecurringRunResult(int Posted, IReadOnlyList<RecurringSkip> Skipped, int Resolved)
{
    public static readonly RecurringRunResult Nothing = new(0, [], 0);
}

public class RecurringPaymentService : IRecurringPaymentService
{
    private readonly AppDbContext _db;
    private readonly ISettingsService _settings;
    private readonly ICurrencyService _currency;

    // The service is a singleton; this keeps two overlapping runs (e.g. the Dashboard
    // appearing twice in quick succession) from both posting the same occurrence.
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public RecurringPaymentService(AppDbContext db, ISettingsService settings, ICurrencyService currency)
    {
        _db = db;
        _settings = settings;
        _currency = currency;
    }

    public async Task<List<RecurringPayment>> GetAllAsync(bool includeInactive = false)
    {
        await _db.InitializeAsync();
        var all = await _db.Connection.Table<RecurringPayment>().ToListAsync();
        return includeInactive ? all : all.Where(r => r.IsActive).ToList();
    }

    public async Task<int> SaveAsync(RecurringPayment payment)
    {
        await _db.InitializeAsync();
        if (payment.Id == 0)
            await _db.Connection.InsertAsync(payment);
        else
            await _db.Connection.UpdateAsync(payment);
        return payment.Id;
    }

    public async Task DeactivateAsync(int id)
    {
        await _db.InitializeAsync();
        var payment = await _db.Connection.Table<RecurringPayment>().FirstOrDefaultAsync(r => r.Id == id);
        if (payment is null) return;
        payment.IsActive = false;
        await _db.Connection.UpdateAsync(payment);
    }

    public async Task<RecurringRunResult> RunDuePaymentsAsync()
    {
        await _runLock.WaitAsync();
        try
        {
            await _db.InitializeAsync();
            var today = DateTime.Today;
            var baseCurrency = (await _settings.GetAsync()).BaseCurrency;
            var posted = 0;
            var resolved = 0;
            var skipped = new List<RecurringSkip>();

            foreach (var payment in (await GetAllAsync()).Where(r => r.NextDueDate.Date <= today))
            {
                var rate = _currency.GetRate(payment.Currency, baseCurrency);

                while (payment.NextDueDate.Date <= today)
                {
                    var dueDate = payment.NextDueDate.Date;
                    var wrotePayment = false;

                    try
                    {
                        // Posting the occurrence and advancing the schedule commit together, so an
                        // interrupted run neither loses an occurrence nor posts it twice.
                        await _db.Connection.RunInTransactionAsync(conn =>
                        {
                            if (!IsPosted(conn, payment.Id, dueDate))
                            {
                                BalanceLedger.Insert(conn, new TransactionRecord
                                {
                                    Type = payment.Type,
                                    AccountId = payment.AccountId,
                                    CategoryId = payment.CategoryId,
                                    OriginalAmount = payment.Amount,
                                    OriginalCurrency = payment.Currency,
                                    ExchangeRate = rate,
                                    BaseAmount = payment.Amount * rate,
                                    BaseCurrencyAtEntry = baseCurrency,
                                    Date = dueDate,
                                    Notes = $"Recurring: {payment.Name}",
                                    RecurringPaymentId = payment.Id
                                }, _currency);
                                wrotePayment = true;
                            }

                            payment.LastRunDate = dueDate;
                            payment.NextDueDate = SafeAddMonth(dueDate, payment.DayOfMonth);
                            conn.Update(payment);
                        });
                    }
                    catch (InsufficientFundsException shortfall)
                    {
                        // Nothing was written: the occurrence stays due, so a top-up is all that is
                        // needed for the next run to post it. Move on to the other payments rather
                        // than retrying this one in a loop that can only fail the same way.
                        skipped.Add(await RecordSkipAsync(payment, dueDate, shortfall));
                        break;
                    }

                    if (wrotePayment)
                        posted++;
                    if (await ResolveSkipAsync(payment.Id, dueDate))
                        resolved++;
                }
            }

            return new RecurringRunResult(posted, skipped, resolved);
        }
        finally
        {
            _runLock.Release();
        }
    }

    public async Task<List<RecurringSkip>> GetUnresolvedSkipsAsync()
    {
        await _db.InitializeAsync();
        var open = await _db.Connection.Table<RecurringSkip>().Where(s => s.ResolvedAt == null).ToListAsync();
        return open.OrderByDescending(s => s.DueDate).ToList();
    }

    public async Task MarkSkipsNotifiedAsync(IEnumerable<RecurringSkip> skips)
    {
        await _db.InitializeAsync();
        foreach (var skip in skips)
        {
            skip.NotifiedAt = DateTime.Now;
            await _db.Connection.UpdateAsync(skip);
        }
    }

    /// <summary>One row per occurrence: a repeat shortfall updates what is already there.</summary>
    private async Task<RecurringSkip> RecordSkipAsync(RecurringPayment payment, DateTime dueDate, InsufficientFundsException shortfall)
    {
        var existing = await _db.Connection.Table<RecurringSkip>()
            .FirstOrDefaultAsync(s => s.RecurringPaymentId == payment.Id && s.DueDate == dueDate && s.ResolvedAt == null);

        if (existing is not null)
        {
            existing.Amount = shortfall.Required;
            existing.Balance = shortfall.Balance;
            existing.Currency = shortfall.Currency;
            existing.LastSkippedAt = DateTime.Now;
            await _db.Connection.UpdateAsync(existing);
            return existing;
        }

        var skip = new RecurringSkip
        {
            RecurringPaymentId = payment.Id,
            DueDate = dueDate,
            AccountId = payment.AccountId,
            Amount = shortfall.Required,
            Balance = shortfall.Balance,
            Currency = shortfall.Currency
        };
        await _db.Connection.InsertAsync(skip);
        return skip;
    }

    /// <summary>Retires the skip for an occurrence that has now posted. True when there was one.</summary>
    private async Task<bool> ResolveSkipAsync(int paymentId, DateTime dueDate)
    {
        var skip = await _db.Connection.Table<RecurringSkip>()
            .FirstOrDefaultAsync(s => s.RecurringPaymentId == paymentId && s.DueDate == dueDate && s.ResolvedAt == null);
        if (skip is null)
            return false;

        skip.ResolvedAt = DateTime.Now;
        await _db.Connection.UpdateAsync(skip);
        return true;
    }

    private static bool IsPosted(SQLiteConnection conn, int paymentId, DateTime dueDate)
    {
        var nextDay = dueDate.AddDays(1);
        return conn.Table<TransactionRecord>()
            .Where(t => t.RecurringPaymentId == paymentId && t.Date >= dueDate && t.Date < nextDay)
            .Count() > 0;
    }

    private static DateTime SafeAddMonth(DateTime from, int dayOfMonth)
    {
        var next = from.AddMonths(1);
        var daysInMonth = DateTime.DaysInMonth(next.Year, next.Month);
        var day = Math.Min(dayOfMonth, daysInMonth);
        return new DateTime(next.Year, next.Month, day);
    }
}
