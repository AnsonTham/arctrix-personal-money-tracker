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
    /// repeatedly and concurrently: runs are serialized, and an occurrence that already has a
    /// posted transaction is never posted again. Returns the number of transactions posted.
    /// </summary>
    Task<int> RunDuePaymentsAsync();
}

public class RecurringPaymentService : IRecurringPaymentService
{
    private readonly AppDbContext _db;
    private readonly ITransactionService _transactions;
    private readonly ISettingsService _settings;
    private readonly ICurrencyService _currency;

    // The service is a singleton; this keeps two overlapping runs (e.g. the Dashboard
    // appearing twice in quick succession) from both posting the same occurrence.
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public RecurringPaymentService(
        AppDbContext db,
        ITransactionService transactions,
        ISettingsService settings,
        ICurrencyService currency)
    {
        _db = db;
        _transactions = transactions;
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

    public async Task<int> RunDuePaymentsAsync()
    {
        await _runLock.WaitAsync();
        try
        {
            await _db.InitializeAsync();
            var today = DateTime.Today;
            var baseCurrency = (await _settings.GetAsync()).BaseCurrency;
            var posted = 0;

            foreach (var payment in (await GetAllAsync()).Where(r => r.NextDueDate.Date <= today))
            {
                var rate = _currency.GetRate(payment.Currency, baseCurrency);

                while (payment.NextDueDate.Date <= today)
                {
                    var dueDate = payment.NextDueDate.Date;

                    if (!await IsPostedAsync(payment.Id, dueDate))
                    {
                        await _transactions.AddAsync(new TransactionRecord
                        {
                            Type = payment.Type,
                            AccountId = payment.AccountId,
                            CategoryId = payment.CategoryId,
                            OriginalAmount = payment.Amount,
                            OriginalCurrency = payment.Currency,
                            ExchangeRate = rate,
                            BaseAmount = payment.Amount * rate,
                            Date = dueDate,
                            Notes = $"Recurring: {payment.Name}",
                            RecurringPaymentId = payment.Id
                        });
                        posted++;
                    }

                    // Saved after each occurrence, so an interrupted run resumes where it stopped.
                    payment.LastRunDate = dueDate;
                    payment.NextDueDate = SafeAddMonth(dueDate, payment.DayOfMonth);
                    await _db.Connection.UpdateAsync(payment);
                }
            }

            return posted;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<bool> IsPostedAsync(int paymentId, DateTime dueDate)
    {
        var nextDay = dueDate.AddDays(1);
        var count = await _db.Connection.Table<TransactionRecord>()
            .Where(t => t.RecurringPaymentId == paymentId && t.Date >= dueDate && t.Date < nextDay)
            .CountAsync();
        return count > 0;
    }

    private static DateTime SafeAddMonth(DateTime from, int dayOfMonth)
    {
        var next = from.AddMonths(1);
        var daysInMonth = DateTime.DaysInMonth(next.Year, next.Month);
        var day = Math.Min(dayOfMonth, daysInMonth);
        return new DateTime(next.Year, next.Month, day);
    }
}
