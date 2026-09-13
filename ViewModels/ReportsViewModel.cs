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
        SelectedYear = DateTime.Now.Year;
        SelectedMonth = DateTime.Now.Month;
    }

    [ObservableProperty] public partial int SelectedYear { get; set; }
    [ObservableProperty] public partial int SelectedMonth { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsGenerating { get; set; }

    public int[] Years { get; } = Enumerable.Range(DateTime.Now.Year - 4, 5).ToArray();
    public string[] Months { get; } = Enumerable.Range(1, 12)
        .Select(m => new DateTime(2000, m, 1).ToString("MMMM"))
        .ToArray();

    [RelayCommand]
    private async Task GenerateAndShare()
    {
        IsGenerating = true;
        StatusMessage = string.Empty;
        try
        {
            var path = await _reports.GenerateMonthlyReportAsync(SelectedYear, SelectedMonth);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Arctrix Monthly Report",
                File = new ShareFile(path)
            });
            StatusMessage = "Report generated.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not generate report: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }
}
