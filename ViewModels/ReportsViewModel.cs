using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class ReportsViewModel : ViewModelBase
{
    private readonly IReportService _reports;

    public ReportsViewModel(IReportService reports)
    {
        _reports = reports;
        Title = "Reports";

        var now = DateTime.Now;
        SelectedYear = now.Year;
        SelectedMonthIndex = now.Month - 1;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodLabel))]
    public partial int SelectedYear { get; set; }

    /// <summary>Zero-based month (0 = January), matching the Months picker.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodLabel))]
    public partial int SelectedMonthIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    [NotifyCanExecuteChangedFor(nameof(OpenReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShareReportCommand))]
    public partial string? LastReportPath { get; set; }

    public int[] Years { get; } = Enumerable.Range(DateTime.Now.Year - 4, 5).ToArray();

    public string[] Months { get; } = Enumerable.Range(1, 12)
        .Select(m => new DateTime(2000, m, 1).ToString("MMMM", CultureInfo.CurrentCulture))
        .ToArray();

    public bool HasReport => LastReportPath is not null;

    public bool HasStatus => StatusMessage.Length > 0;

    /// <summary>False on Android and iOS, where PDF generation isn't available yet.</summary>
    public bool IsReportingSupported => _reports.IsSupported;

    public bool IsReportingUnavailable => !_reports.IsSupported;

    public string PeriodLabel => SelectedMonthIndex is >= 0 and < 12
        ? new DateTime(SelectedYear, SelectedMonthIndex + 1, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture)
        : string.Empty;

    // A report on screen always matches the chosen period.
    partial void OnSelectedYearChanged(int value) => ClearReport();

    partial void OnSelectedMonthIndexChanged(int value) => ClearReport();

    [RelayCommand]
    private async Task Generate()
    {
        if (!_reports.IsSupported)
            return;

        StatusMessage = string.Empty;
        try
        {
            LastReportPath = await _reports.GenerateMonthlyReportAsync(SelectedYear, SelectedMonthIndex + 1);
            StatusMessage = $"{PeriodLabel} report is ready.";
        }
        catch (Exception ex)
        {
            LastReportPath = null;
            StatusMessage = $"Could not generate the report: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasReport))]
    private async Task OpenReport()
    {
        if (LastReportPath is not string path)
            return;

        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest("Arctrix monthly report", new ReadOnlyFile(path)));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open the report: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasReport))]
    private async Task ShareReport()
    {
        if (LastReportPath is not string path)
            return;

        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Arctrix monthly report",
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not share the report: {ex.Message}";
        }
    }

    private void ClearReport()
    {
        LastReportPath = null;
        StatusMessage = string.Empty;
    }
}
