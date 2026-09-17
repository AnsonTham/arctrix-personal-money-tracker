using Arctrix.PersonalMoneyTracker.Services;
using Foundation;
using UIKit;
using Vision;

namespace Arctrix.PersonalMoneyTracker;

/// <summary>
/// Receipt OCR with Apple's Vision framework (VNRecognizeTextRequest), which runs on the device and
/// needs no extra package. Not yet run on a device: no Mac is available to build for iOS.
/// </summary>
public sealed class VisionReceiptOcrService : IReceiptOcrService
{
    public Task<string> RecognizeTextAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Recognize(imagePath, cancellationToken), cancellationToken);

    private static string Recognize(string imagePath, CancellationToken cancellationToken)
    {
        // The upright image size turns Vision's normalized coordinates back into proportional pixels.
        using var image = UIImage.FromFile(imagePath);
        if (image is null)
            return string.Empty;
        var width = (double)image.Size.Width;
        var height = (double)image.Size.Height;

        using var request = new VNRecognizeTextRequest((_, _) => { })
        {
            RecognitionLevel = VNRequestTextRecognitionLevel.Accurate,
            UsesLanguageCorrection = true
        };
        using var url = NSUrl.FromFilename(imagePath);
        using var handler = new VNImageRequestHandler(url, new NSDictionary());
        if (!handler.Perform([request], out var error))
            throw new InvalidOperationException(error?.LocalizedDescription ?? "Text recognition failed.");

        cancellationToken.ThrowIfCancellationRequested();

        var observations = request.GetResults<VNRecognizedTextObservation>() ?? [];
        return OcrLayout.JoinIntoRows(observations
            .Select(o => ToFragment(o, width, height))
            .OfType<OcrFragment>());
    }

    private static OcrFragment? ToFragment(VNRecognizedTextObservation observation, double width, double height)
    {
        var text = observation.TopCandidates(1).FirstOrDefault()?.String;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Vision's origin is the bottom-left corner; flip y so it grows downward like other platforms.
        return OcrFragment.FromCorners(
            text,
            observation.TopLeft.X * width, (1 - observation.TopLeft.Y) * height,
            observation.TopRight.X * width, (1 - observation.TopRight.Y) * height,
            observation.BottomRight.X * width, (1 - observation.BottomRight.Y) * height,
            observation.BottomLeft.X * width, (1 - observation.BottomLeft.Y) * height);
    }
}
