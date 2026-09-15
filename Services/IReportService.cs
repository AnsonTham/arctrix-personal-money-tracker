namespace Arctrix.PersonalMoneyTracker.Services;

public interface IReportService
{
    /// <summary>
    /// False where PDF generation isn't available. QuestPDF only renders on desktop, so this is
    /// currently true on Windows only.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>Builds a monthly PDF summary and returns the saved file path.</summary>
    Task<string> GenerateMonthlyReportAsync(int year, int month);
}
