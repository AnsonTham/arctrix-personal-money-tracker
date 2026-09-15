namespace Arctrix.PersonalMoneyTracker.Helpers;

/// <summary>
/// Category line icons (Resources/Images/cat_*.svg), drawn in the same 24px, 1.8-stroke style as
/// the navigation icons. Categories store an icon key such as "food"; this maps it to the image.
/// </summary>
public static class CategoryIcons
{
    public const string Other = "other";

    private static readonly HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        "food", "transport", "shopping", "bills", "drinks", "entertainment", Other, "investment", "income", "transfer"
    };

    /// <summary>Image file for an icon key; unknown keys (including legacy emoji) get the generic icon.</summary>
    public static string FileFor(string? key)
        => $"cat_{(key is not null && Keys.Contains(key) ? key.ToLowerInvariant() : Other)}.png";
}
