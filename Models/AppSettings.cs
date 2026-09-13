using SQLite;

namespace Arctrix.PersonalMoneyTracker.Models;

/// <summary>
/// Single-row table (Id is always 1) holding app-wide preferences.
/// </summary>
public class AppSettings
{
    [PrimaryKey]
    public int Id { get; set; } = 1;

    [MaxLength(3)]
    public string BaseCurrency { get; set; } = "MYR";

    /// <summary>Reserved for future light/dark toggle; app currently ships dark-only.</summary>
    public bool UseDarkTheme { get; set; } = true;

    public bool NotificationsEnabled { get; set; } = true;

    public DateTime? LastBackupDate { get; set; }
}
