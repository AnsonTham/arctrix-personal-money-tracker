using SQLite;

namespace Arctrix.PersonalMoneyTracker.Models;

/// <summary>
/// A date that doesn't count as a working day. Used by the last-working-day recurrence rule, and
/// kept as a plain editable list rather than computed: Malaysian holidays include state ones, and
/// Islamic and lunar dates are only fixed once officially announced. The user can correct the list
/// at any time, which keeps the app offline and free of an external calendar service.
/// </summary>
public class PublicHoliday
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>The holiday itself; only the date part is used.</summary>
    [Indexed]
    public DateTime Date { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Malaysia's national public holidays for 2026 and 2027, as a starting point.
    ///
    /// The fixed-date ones are certain. The Islamic dates (Hari Raya, Awal Muharram, Maulidur
    /// Rasul) and the lunar ones (Chinese New Year, Wesak, Deepavali) are the expected dates and
    /// are confirmed by official announcement closer to the time, so they are worth checking.
    /// State holidays are deliberately left out - add the ones that apply where you are.
    /// </summary>
    public static readonly (DateTime Date, string Name)[] MalaysiaDefaults =
    [
        // 2026
        (new DateTime(2026, 1, 1),   "New Year's Day"),
        (new DateTime(2026, 2, 17),  "Chinese New Year"),
        (new DateTime(2026, 2, 18),  "Chinese New Year (second day)"),
        (new DateTime(2026, 3, 20),  "Hari Raya Aidilfitri"),
        (new DateTime(2026, 3, 21),  "Hari Raya Aidilfitri (second day)"),
        (new DateTime(2026, 5, 1),   "Labour Day"),
        (new DateTime(2026, 5, 27),  "Hari Raya Haji"),
        (new DateTime(2026, 5, 31),  "Wesak Day"),
        (new DateTime(2026, 6, 1),   "Agong's Birthday"),
        (new DateTime(2026, 6, 16),  "Awal Muharram"),
        (new DateTime(2026, 8, 25),  "Maulidur Rasul"),
        (new DateTime(2026, 8, 31),  "Merdeka Day"),
        (new DateTime(2026, 9, 16),  "Malaysia Day"),
        (new DateTime(2026, 11, 8),  "Deepavali"),
        (new DateTime(2026, 12, 25), "Christmas Day"),

        // 2027
        (new DateTime(2027, 1, 1),   "New Year's Day"),
        (new DateTime(2027, 2, 6),   "Chinese New Year"),
        (new DateTime(2027, 2, 7),   "Chinese New Year (second day)"),
        (new DateTime(2027, 3, 10),  "Hari Raya Aidilfitri"),
        (new DateTime(2027, 3, 11),  "Hari Raya Aidilfitri (second day)"),
        (new DateTime(2027, 5, 1),   "Labour Day"),
        (new DateTime(2027, 5, 17),  "Hari Raya Haji"),
        (new DateTime(2027, 5, 20),  "Wesak Day"),
        (new DateTime(2027, 6, 6),   "Awal Muharram"),
        (new DateTime(2027, 6, 7),   "Agong's Birthday"),
        (new DateTime(2027, 8, 15),  "Maulidur Rasul"),
        (new DateTime(2027, 8, 31),  "Merdeka Day"),
        (new DateTime(2027, 9, 16),  "Malaysia Day"),
        (new DateTime(2027, 10, 28), "Deepavali"),
        (new DateTime(2027, 12, 25), "Christmas Day"),
    ];
}
