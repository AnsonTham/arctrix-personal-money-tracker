using SQLite;

namespace Arctrix.PersonalMoneyTracker.Models;

/// <summary>
/// A place money lives: bank account, cash wallet, e-wallet, or investment account.
/// Balance is kept in the account's own currency; the base-currency value is
/// derived on demand using Settings.BaseCurrency and each transaction's FX snapshot.
/// </summary>
public class Account
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public AccountType Type { get; set; } = AccountType.Bank;

    [MaxLength(3)]
    public string Currency { get; set; } = "MYR";

    public decimal Balance { get; set; }

    /// <summary>Hex color used for the account's accent/avatar, e.g. "#22D3A2".</summary>
    [MaxLength(9)]
    public string ColorHex { get; set; } = "#8F98A7";

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
