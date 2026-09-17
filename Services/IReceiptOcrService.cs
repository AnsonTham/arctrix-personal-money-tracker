namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>
/// On-device text recognition for receipt photos. Implemented per platform: Google ML Kit on
/// Android, Apple Vision on iOS. Desktop builds don't include receipt scanning.
/// </summary>
public interface IReceiptOcrService
{
    /// <summary>
    /// Recognizes the text in a photo, one visual row per line from top to bottom. Returns an empty
    /// string when the photo contains no readable text.
    /// </summary>
    Task<string> RecognizeTextAsync(string imagePath, CancellationToken cancellationToken = default);
}
