namespace Arctrix.PersonalMoneyTracker.Helpers;

/// <summary>
/// Receipt scanning is a phone and tablet feature (ML Kit on Android, Vision on iOS). Desktop builds
/// don't compile its pages or OCR at all; this guards the few shared commands that lead to it.
/// </summary>
public static class ReceiptScanning
{
    public static bool IsSupported =>
        DeviceInfo.Current.Platform == DevicePlatform.Android || DeviceInfo.Current.Platform == DevicePlatform.iOS;
}
