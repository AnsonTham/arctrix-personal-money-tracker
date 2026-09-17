using SQLite;

namespace Arctrix.PersonalMoneyTracker.Models;

/// <summary>
/// A single money movement. Renamed from the old "TransactionItem" to avoid
/// clashing with System.Transactions.TransactionRecord-like APIs.
///
/// Amounts are stored twice on purpose:
///   - OriginalAmount / OriginalCurrency: exactly what the user entered.
///   - ExchangeRate: the OriginalCurrency -> BaseCurrency rate at the time
///     of entry (1.0 if same currency). This is a snapshot, never recomputed,
///     so historical reports stay accurate even if rates change later.
///   - BaseAmount: OriginalAmount * ExchangeRate, in Settings.BaseCurrency.
///     Pre-computed so history/analytics never need live FX lookups.
/// </summary>
public class TransactionRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public TransactionType Type { get; set; } = TransactionType.Expense;

    [Indexed]
    public int AccountId { get; set; }

    /// <summary>Destination account for Transfer transactions; otherwise null.</summary>
    public int? ToAccountId { get; set; }

    [Indexed]
    public int CategoryId { get; set; }

    public decimal OriginalAmount { get; set; }

    [MaxLength(3)]
    public string OriginalCurrency { get; set; } = "MYR";

    public decimal ExchangeRate { get; set; } = 1.0m;

    public decimal BaseAmount { get; set; }

    /// <summary>
    /// The base currency BaseAmount was computed in. The base currency can be changed later, so
    /// period totals group and label amounts by this instead of today's setting. Rows saved
    /// before this column existed are stamped on startup (see AppDbContext).
    /// </summary>
    [MaxLength(3)]
    public string? BaseCurrencyAtEntry { get; set; }

    [Indexed]
    public DateTime Date { get; set; } = DateTime.Now;

    [MaxLength(500)]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// How much AccountId's balance moved, in that account's own currency. Recorded when the
    /// transaction is saved so the effect can be reversed exactly. Null on rows saved before
    /// this column existed; those were applied with OriginalAmount.
    /// </summary>
    public decimal? AccountAmount { get; set; }

    /// <summary>Same as <see cref="AccountAmount"/>, for ToAccountId.</summary>
    public decimal? ToAccountAmount { get; set; }

    /// <summary>Set when this row was generated automatically from a RecurringPayment.</summary>
    public int? RecurringPaymentId { get; set; }

    /// <summary>
    /// Photo of the receipt this transaction was scanned from, relative to the app data folder
    /// (see ReceiptPhotoStore); null when there is none. Added as a nullable column, so existing
    /// rows are unaffected.
    /// </summary>
    [MaxLength(260)]
    public string? ReceiptImagePath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
