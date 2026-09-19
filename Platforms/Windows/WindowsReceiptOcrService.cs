using Arctrix.PersonalMoneyTracker.Services;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Arctrix.PersonalMoneyTracker;

/// <summary>
/// Receipt OCR with Windows' built-in recognizer (Windows.Media.Ocr): on-device, no key, no network,
/// and already installed with the display language. Desktop has no scanning UI - this exists so a
/// receipt photo sent to the Telegram bot is read on the PC the same way the phone reads one.
/// </summary>
public sealed class WindowsReceiptOcrService : IReceiptOcrService
{
    public async Task<string> RecognizeTextAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
            ?? throw new NotSupportedException("Windows has no OCR language pack installed.");

        using var stream = new InMemoryRandomAccessStream();
        await WriteAsync(stream, await File.ReadAllBytesAsync(imagePath, cancellationToken));
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            FitToEngine(decoder),
            // Phone photos carry their rotation in metadata, so honour it before reading text.
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken);

        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return FixDigits(OcrLayout.JoinIntoRows(Fragments(result)));
    }

    /// <summary>
    /// Windows' recognizer reads the slashed and squared zeros of receipt printers as letters
    /// ("TOTAL RM 28.2ø", "14/ø9/2ø26"), which would hide the total and the date from the parser.
    /// Only tokens that are already numbers are touched: a token needs a digit, and may otherwise
    /// contain nothing but separators and the handful of letters that stand in for digits, so
    /// ordinary words - including a lone "O" in "Kopi O Ais" - are left exactly as they were.
    /// </summary>
    private static string FixDigits(string text) =>
        string.Join('\n', text.Split('\n').Select(line =>
            string.Join(' ', line.Split(' ').Select(token => LooksNumeric(token) ? ToDigits(token) : token))));

    private static bool LooksNumeric(string token) =>
        token.Any(char.IsDigit) && token.All(c => char.IsDigit(c) || Confusable(c) is not null || ".,:/-%".Contains(c));

    private static string ToDigits(string token) =>
        new(token.Select(c => Confusable(c) ?? c).ToArray());

    private static char? Confusable(char c) => c switch
    {
        'o' or 'O' or 'ø' or 'Ø' or 'e' or 'E' or 'Q' or 'D' => '0',
        'l' or 'I' or '|' => '1',
        _ => null
    };

    /// <summary>The recognizer rejects images past <see cref="OcrEngine.MaxImageDimension"/>; scale to fit, keeping the aspect ratio.</summary>
    private static BitmapTransform FitToEngine(BitmapDecoder decoder)
    {
        var max = (double)OcrEngine.MaxImageDimension;
        var scale = Math.Min(1.0, Math.Min(max / decoder.PixelWidth, max / decoder.PixelHeight));
        return new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant
        };
    }

    private static async Task WriteAsync(IRandomAccessStream stream, byte[] bytes)
    {
        using var writer = new DataWriter(stream.GetOutputStreamAt(0));
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
    }

    /// <summary>
    /// One fragment per recognized line, boxed by its words. Windows reports upright rectangles, so a
    /// line's left and right edge midpoints sit at the same height; OcrLayout still rebuilds the rows,
    /// because a label and its price are usually separate lines.
    /// </summary>
    private static IEnumerable<OcrFragment> Fragments(OcrResult result)
    {
        foreach (var line in result.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text) || line.Words.Count == 0)
                continue;

            var left = line.Words.Min(w => w.BoundingRect.Left);
            var right = line.Words.Max(w => w.BoundingRect.Right);
            var top = line.Words.Min(w => w.BoundingRect.Top);
            var bottom = line.Words.Max(w => w.BoundingRect.Bottom);
            var middle = (top + bottom) / 2;

            yield return new OcrFragment(line.Text, left, middle, right, middle, bottom - top);
        }
    }
}
