using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

public interface IRecurringPaymentService
{
    Task<List<RecurringPayment>> GetAllAsync(bool includeInactive = false);
    Task<int> SaveAsync(RecurringPayment payment);
    Task DeactivateAsync(int id);

    /// <summary>
    /// Posts a TransactionRecord for every recurring payment whose NextDueDate
    /// has arrived, then advances NextDueDate by one month. Safe to call on
    /// every app launch - it is a no-op for payments that are not yet due.
    /// </summary>
    Task<int> RunDuePaymentsAsync();
}

public class RecurringPaymentService : IRecurringPaymentService
{
    private readonly AppDbContext _db;
    private readonly ITransactionService _transactions;

    public RecurringPaymentService(AppDbContext db, ITransactionService transactions)
    {
        _db = db;
        _transactions = transactions;
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
        await _db.InitializeAsync();
        var due = (await GetAllAsync())
            .Where(r => r.NextDueDate.Date <= DateTime.Now.Date)
            .ToList();

        foreach (var payment in due)
        {
            var record = new TransactionRecord
            {
                Type = payment.Type,
                AccountId = payment.AccountId,
                CategoryId = payment.CategoryId,
                OriginalAmount = payment.Amount,
                OriginalCurrency = payment.Currency,
                ExchangeRate = 1.0m,
                BaseAmount = payment.Amount,
                Date = payment.NextDueDate,
                Notes = $"Recurring: {payment.Name}",
                RecurringPaymentId = payment.Id
            };
            await _transactions.AddAsync(record);

            payment.LastRunDate = payment.NextDueDate;
            payment.NextDueDate = SafeAddMonth(payment.NextDueDate, payment.DayOfMonth);
            await _db.Connection.UpdateAsync(payment);
        }

        return due.Count;
    }

    private static DateTime SafeAddMonth(DateTime from, int dayOfMonth)
    {
        var next = from.AddMonths(1);
        var daysInMonth = DateTime.DaysInMonth(next.Year, next.Month);
        var day = Math.Min(dayOfMonth, daysInMonth);
        return new DateTime(next.Year, next.Month, day);
    }
}
