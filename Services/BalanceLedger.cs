using SQLite;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>
/// Writes transaction rows together with the account balance changes they cause. Every method
/// takes the synchronous connection handed out by SQLiteAsyncConnection.RunInTransactionAsync,
/// so a row and its balance updates commit together or roll back together.
/// </summary>
internal static class BalanceLedger
{
    public static void Insert(SQLiteConnection conn, TransactionRecord transaction, ICurrencyService currency, bool allowOverdraw = false)
    {
        SetAccountAmounts(conn, transaction, currency);
        RequireFunds(conn, transaction, allowOverdraw);
        conn.Insert(transaction);
        Apply(conn, transaction, sign: 1);
    }

    /// <summary>Reverses the stored row's effect, applies the updated one, and saves it.</summary>
    public static void Update(SQLiteConnection conn, TransactionRecord original, TransactionRecord updated, ICurrencyService currency, bool allowOverdraw = false)
    {
        // Reverse what is actually stored, not a copy the caller loaded earlier.
        Apply(conn, conn.Find<TransactionRecord>(updated.Id) ?? original, sign: -1);
        SetAccountAmounts(conn, updated, currency);
        RequireFunds(conn, updated, allowOverdraw);
        Apply(conn, updated, sign: 1);
        conn.Update(updated);
    }

    /// <summary>
    /// Refuses a transaction that would take an account below zero. This sits here, rather than in
    /// the callers, because every write path - the form, receipt scanning, the Telegram bot and the
    /// recurring auto-poster - reaches the database through Insert and Update: a path that forgets
    /// to check cannot exist. Throwing inside the caller's database transaction rolls back the row
    /// and the balances together.
    ///
    /// Amounts here are already in each account's own currency (see <see cref="SetAccountAmounts"/>).
    /// </summary>
    private static void RequireFunds(SQLiteConnection conn, TransactionRecord transaction, bool allowOverdraw)
    {
        if (allowOverdraw)
            return;

        foreach (var (accountId, delta) in Effects(transaction))
        {
            if (delta >= 0)
                continue;

            var account = conn.Find<Account>(accountId);
            // A missing account can't be overdrawn; Apply skips it too.
            if (account is null || account.Balance + delta >= 0)
                continue;

            throw new InsufficientFundsException(account.Name, account.Currency, account.Balance, -delta);
        }
    }

    /// <summary>Reverses the stored row's effect and deletes it. A no-op if it's already gone.</summary>
    public static void Delete(SQLiteConnection conn, TransactionRecord transaction)
    {
        var stored = conn.Find<TransactionRecord>(transaction.Id);
        if (stored is null)
            return;

        Apply(conn, stored, sign: -1);
        conn.Delete(stored);
    }

    /// <summary>
    /// A transaction's change to each affected account's balance, in that account's currency.
    /// Uses the per-account amounts recorded on save; rows saved before those columns existed
    /// were applied with OriginalAmount, so they fall back to it.
    /// </summary>
    public static IEnumerable<(int AccountId, decimal Delta)> Effects(TransactionRecord t)
    {
        var fromAmount = t.AccountAmount ?? t.OriginalAmount;

        switch (t.Type)
        {
            case TransactionType.Income:
                yield return (t.AccountId, fromAmount);
                break;

            case TransactionType.Expense:
                yield return (t.AccountId, -fromAmount);
                break;

            case TransactionType.Transfer:
            case TransactionType.Investment:
                yield return (t.AccountId, -fromAmount);
                if (t.ToAccountId is int toId)
                    yield return (toId, t.ToAccountAmount ?? t.OriginalAmount);
                break;
        }
    }

    private static void Apply(SQLiteConnection conn, TransactionRecord t, int sign)
    {
        foreach (var (accountId, delta) in Effects(t))
        {
            var account = conn.Find<Account>(accountId);
            if (account is null)
                continue;

            account.Balance += sign * delta;
            conn.Update(account);
        }
    }

    /// <summary>
    /// Records how far each affected account moves, in that account's own currency: a USD 100
    /// expense on a MYR account moves the balance by MYR 470, not 100.
    /// </summary>
    private static void SetAccountAmounts(SQLiteConnection conn, TransactionRecord t, ICurrencyService currency)
    {
        t.AccountAmount = InAccountCurrency(conn, t, t.AccountId, currency);
        t.ToAccountAmount = t.ToAccountId is int toId ? InAccountCurrency(conn, t, toId, currency) : null;
    }

    private static decimal InAccountCurrency(SQLiteConnection conn, TransactionRecord t, int accountId, ICurrencyService currency)
    {
        var account = conn.Find<Account>(accountId);
        if (account is null)
            return t.OriginalAmount;

        var converted = currency.Convert(t.OriginalAmount, t.OriginalCurrency, account.Currency);
        return Math.Round(converted, 2, MidpointRounding.AwayFromZero);
    }
}
