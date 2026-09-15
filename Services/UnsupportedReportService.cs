namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>
/// Registered on platforms without PDF support (Android, iOS), so the Reports page shows a
/// notice instead of loading QuestPDF and crashing.
/// </summary>
public class UnsupportedReportService : IReportService
{
    public bool IsSupported => false;

    public Task<string> GenerateMonthlyReportAsync(int year, int month)
        => throw new PlatformNotSupportedException("PDF reports are available on Windows for now.");
}
