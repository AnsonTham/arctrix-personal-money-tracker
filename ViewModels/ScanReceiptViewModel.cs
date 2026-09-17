using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public enum ReceiptScanStage
{
    Ready,
    Capturing,
    Reading,
    Unavailable
}

/// <summary>
/// Photographs a receipt, reads it on the device, and opens the transaction form pre-filled with
/// what was found. Nothing is saved here: the form is where the user checks and saves.
/// </summary>
public partial class ScanReceiptViewModel : ViewModelBase
{
    // Long edge of the stored photo: plenty for OCR on receipt text, without multi-megabyte files.
    private const int MaxPhotoEdge = 2000;
    private const int PhotoQuality = 85;

    private readonly IReceiptOcrService _ocr;
    private readonly IReceiptPhotoStore _photos;
    private bool _started;

    public ScanReceiptViewModel(IReceiptOcrService ocr, IReceiptPhotoStore photos)
    {
        _ocr = ocr;
        _photos = photos;
        Title = "Scan receipt";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsCapturing), nameof(IsReading), nameof(IsUnavailable))]
    public partial ReceiptScanStage Stage { get; set; } = ReceiptScanStage.Ready;

    [ObservableProperty] public partial string? PhotoPreview { get; set; }
    [ObservableProperty] public partial string UnavailableMessage { get; set; } = string.Empty;

    public bool IsReady => Stage == ReceiptScanStage.Ready;
    public bool IsCapturing => Stage == ReceiptScanStage.Capturing;
    public bool IsReading => Stage == ReceiptScanStage.Reading;
    public bool IsUnavailable => Stage == ReceiptScanStage.Unavailable;

    /// <summary>Opens the camera straight away the first time the page appears.</summary>
    public Task StartAsync()
    {
        if (_started)
            return Task.CompletedTask;
        _started = true;
        return TakePhotoCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task TakePhoto()
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            ShowUnavailable("This device doesn't have a camera the app can use.");
            return;
        }

        Stage = ReceiptScanStage.Capturing;
        FileResult? photo;
        try
        {
            photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = "Photograph the receipt",
                MaximumWidth = MaxPhotoEdge,
                MaximumHeight = MaxPhotoEdge,
                CompressionQuality = PhotoQuality,
                RotateImage = true
            });
        }
        catch (PermissionException)
        {
            ShowUnavailable("Camera access is turned off. Allow it in your device settings to scan receipts, or enter the transaction manually.");
            return;
        }
        catch (Exception ex)
        {
            ShowUnavailable($"The camera couldn't be opened: {ex.Message}");
            return;
        }

        // The user backed out of the camera: stay here so they can try again or type it in.
        if (photo is null)
        {
            Stage = ReceiptScanStage.Ready;
            return;
        }

        string storedPath;
        try
        {
            storedPath = await _photos.SaveAsync(photo);
        }
        catch (Exception ex)
        {
            ShowUnavailable($"The photo couldn't be saved: {ex.Message}");
            return;
        }

        PhotoPreview = _photos.GetFullPath(storedPath);
        Stage = ReceiptScanStage.Reading;

        ReceiptScanResult? scan = null;
        try
        {
            var text = await _ocr.RecognizeTextAsync(PhotoPreview);
            var parsed = ReceiptParser.Parse(text, DateTime.Today);
            if (parsed.HasUsefulData)
                scan = parsed;
        }
        catch (Exception ex)
        {
            // Unreadable is not an error for the user: the form opens empty with the photo attached.
            Debug.WriteLine($"Receipt text recognition failed: {ex}");
        }

        await Shell.Current.GoToAsync($"../{Routes.AddTransaction}", new ShellNavigationQueryParameters
        {
            [Routes.ReceiptScanParam] = new ReceiptDraft(storedPath, scan)
        });
    }

    [RelayCommand]
    private Task EnterManually() => Shell.Current.GoToAsync($"../{Routes.AddTransaction}");

    private void ShowUnavailable(string message)
    {
        UnavailableMessage = message;
        Stage = ReceiptScanStage.Unavailable;
    }
}
