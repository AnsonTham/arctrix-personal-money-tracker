using System.Globalization;

namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>
/// Thrown when posting a transaction would take an account's balance below zero. Raised inside the
/// database transaction that would have applied it, so nothing is written when it is thrown.
///
/// Callers acting on the user's behalf may retry with allowOverdraw set, after asking; the recurring
/// auto-poster never does, and skips the occurrence instead.
/// </summary>
public sealed class InsufficientFundsException : Exception
{
    public InsufficientFundsException(string accountName, string currency, decimal balance, decimal required)
        : base($"{accountName} holds {Format(currency, balance)}, which doesn't cover {Format(currency, required)}.")
    {
        AccountName = accountName;
        Currency = currency;
        Balance = balance;
        Required = required;
    }

    public string AccountName { get; }

    /// <summary>The account's own currency: balance and amounts here are all in it.</summary>
    public string Currency { get; }

    public decimal Balance { get; }

    /// <summary>How much the account had to give up.</summary>
    public decimal Required { get; }

    /// <summary>What is missing - always positive.</summary>
    public decimal Shortfall => Required - Balance;

    private static string Format(string currency, decimal amount) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
