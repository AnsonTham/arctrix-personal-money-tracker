using SQLite;

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

    /// <summary>Segoe/Material glyph or emoji shown as the category icon.</summary>
    [MaxLength(8)]
    public string Icon { get; set; } = "•";

    [MaxLength(9)]
    public string ColorHex { get; set; } = "#8F98A7";

    /// <summary>Which transaction type this category is normally used for.</summary>
    public TransactionType DefaultType { get; set; } = TransactionType.Expense;

    /// <summary>Seeded categories cannot be deleted, only hidden.</summary>
    public bool IsSystem { get; set; }

    public bool IsArchived { get; set; }

    public static readonly (string Name, string Icon, TransactionType Type)[] QuickAddDefaults =
    {
        ("Food",          "🍔", TransactionType.Expense),
        ("Transport",     "🚗", TransactionType.Expense),
        ("Shopping",      "🛍️", TransactionType.Expense),
        ("Bills",         "🧾", TransactionType.Expense),
        ("Drinks",        "🥤", TransactionType.Expense),
        ("Entertainment", "🎬", TransactionType.Expense),
        ("Others",        "•",  TransactionType.Expense),
        ("Investment",    "📈", TransactionType.Investment),
        ("Income",        "💰", TransactionType.Income),
        ("Transfer",      "↔️", TransactionType.Transfer),
    };
}
