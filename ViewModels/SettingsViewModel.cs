using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;

    public SettingsViewModel(ISettingsService settings, ICurrencyService currency)
    {
        _settings = settings;
        SupportedCurrencies = currency.SupportedCurrencies;
        Title = "Settings";
    }

    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";
    [ObservableProperty] public partial bool NotificationsEnabled { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;

    public IReadOnlyList<string> SupportedCurrencies { get; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var settings = await _settings.GetAsync();
        BaseCurrency = settings.BaseCurrency;
        NotificationsEnabled = settings.NotificationsEnabled;
    }

    [RelayCommand]
    private async Task Save()
    {
        var settings = await _settings.GetAsync();
        settings.BaseCurrency = BaseCurrency;
        settings.NotificationsEnabled = NotificationsEnabled;
        await _settings.SaveAsync(settings);
        StatusMessage = "Saved.";
    }
}
