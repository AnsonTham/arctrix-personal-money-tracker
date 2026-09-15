using SQLite;
using Arctrix.PersonalMoneyTracker.Helpers;

namespace Arctrix.PersonalMoneyTracker.Models;

/// <summary>
/// A spending/income bucket, e.g. Food, Transport, Income. Seeded with the
/// Quick Add defaults on first run; users may add their own later.
/// </summary>
public class Category
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(40)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Icon key, e.g. "food" (see <see cref="CategoryIcons"/>). Databases created before the line
    /// icons stored emoji here; AppDbContext updates the seeded categories on startup.
    /// </summary>
    [MaxLength(40)]
    public string Icon { get; set; } = CategoryIcons.Other;

    [Ignore]
    public string IconFile => CategoryIcons.FileFor(Icon);

    [MaxLength(9)]
    public string ColorHex { get; set; } = "#8F98A7";

    /// <summary>Which transaction type this category is normally used for.</summary>
    public TransactionType DefaultType { get; set; } = TransactionType.Expense;

    /// <summary>Seeded categories cannot be deleted, only hidden.</summary>
    public bool IsSystem { get; set; }

    public bool IsArchived { get; set; }

    public static readonly (string Name, string Icon, TransactionType Type)[] QuickAddDefaults =
    {
        ("Food",          "food",          TransactionType.Expense),
        ("Transport",     "transport",     TransactionType.Expense),
        ("Shopping",      "shopping",      TransactionType.Expense),
        ("Bills",         "bills",         TransactionType.Expense),
        ("Drinks",        "drinks",        TransactionType.Expense),
        ("Entertainment", "entertainment", TransactionType.Expense),
        ("Others",        CategoryIcons.Other, TransactionType.Expense),
        ("Investment",    "investment",    TransactionType.Investment),
        ("Income",        "income",        TransactionType.Income),
        ("Transfer",      "transfer",      TransactionType.Transfer),
    };
}
