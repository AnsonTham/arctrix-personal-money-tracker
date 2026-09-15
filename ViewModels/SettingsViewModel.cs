using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>One currency's rate against the base currency, for display.</summary>
public record ExchangeRateRow(string Currency, decimal RateToBase, string BaseCurrency)
{
    public string FromLabel => $"1 {Currency}";

    public string ToLabel => $"{RateToBase:0.####} {BaseCurrency}";
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ICurrencyService _currency;

    public SettingsViewModel(ISettingsService settings, ICurrencyService currency)
    {
        _settings = settings;
        _currency = currency;
        SupportedCurrencies = currency.SupportedCurrencies;
        Title = "Settings";
    }

    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";
    [ObservableProperty] public partial IReadOnlyList<ExchangeRateRow> ExchangeRates { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; } = string.Empty;

    public IReadOnlyList<string> SupportedCurrencies { get; }

    public bool HasStatus => StatusMessage.Length > 0;

    public string AppVersion => $"Version {AppInfo.Current.VersionString}";

    public string DataFolder => FileSystem.AppDataDirectory;

    partial void OnBaseCurrencyChanged(string value)
    {
        StatusMessage = string.Empty;
        ExchangeRates = SupportedCurrencies
            .Where(c => !string.Equals(c, value, StringComparison.OrdinalIgnoreCase))
            .Select(c => new ExchangeRateRow(c, _currency.GetRate(c, value), value))
            .ToList();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var settings = await _settings.GetAsync();
        BaseCurrency = settings.BaseCurrency;
        OnBaseCurrencyChanged(BaseCurrency);
    }

    [RelayCommand]
    private async Task Save()
    {
        var settings = await _settings.GetAsync();
        settings.BaseCurrency = BaseCurrency;
        await _settings.SaveAsync(settings);
        StatusMessage = "Settings saved.";
    }
}
