using Android.Gms.Extensions;
using Arctrix.PersonalMoneyTracker.Services;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;
using RecognizedText = Xamarin.Google.MLKit.Vision.Text.Text;

namespace Arctrix.PersonalMoneyTracker;

/// <summary>
/// Receipt OCR with Google ML Kit's bundled Latin text model: runs entirely on the device, with no
/// API key, network access or model download.
/// </summary>
public sealed class MlKitReceiptOcrService : IReceiptOcrService
{
    public async Task<string> RecognizeTextAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var file = new Java.IO.File(imagePath);
        using var uri = Android.Net.Uri.FromFile(file) ?? throw new IOException($"Can't open {imagePath}.");
        // Reads the photo's orientation metadata, so text is upright however the phone was held.
        using var image = InputImage.FromFilePath(Android.App.Application.Context, uri);

        var recognizer = TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
        try
        {
            var result = await recognizer.Process(image).AsAsync<RecognizedText>();
            cancellationToken.ThrowIfCancellationRequested();
            return OcrLayout.JoinIntoRows(Fragments(result));
        }
        finally
        {
            recognizer.Close();
            recognizer.Dispose();
        }
    }

    private static IEnumerable<OcrFragment> Fragments(RecognizedText? result)
    {
        if (result is null)
            yield break;

        foreach (var block in result.TextBlocks)
        {
            foreach (var line in block.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text))
                    continue;

                // Corner points follow the text's slant (top-left, top-right, bottom-right, bottom-left).
                if (line.GetCornerPoints() is { Length: 4 } c)
                {
                    yield return OcrFragment.FromCorners(line.Text, c[0].X, c[0].Y, c[1].X, c[1].Y, c[2].X, c[2].Y, c[3].X, c[3].Y);
                }
                else if (line.BoundingBox is { } box)
                {
                    yield return new OcrFragment(line.Text, box.Left, box.CenterY(), box.Right, box.CenterY(), box.Height());
                }
            }
        }
    }
}
