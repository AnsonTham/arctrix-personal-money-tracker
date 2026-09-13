using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class AddAccountViewModel : ViewModelBase
{
    private readonly IAccountService _accounts;
    private readonly ISettingsService _settings;
    private bool _loaded;

    public AddAccountViewModel(IAccountService accounts, ISettingsService settings, ICurrencyService currency)
    {
        _accounts = accounts;
        _settings = settings;
        Title = "Add account";

        SupportedCurrencies = currency.SupportedCurrencies;
        TypeOptions = Enum.GetValues<AccountType>().Select(t => new AccountTypeOption(t)).ToList();
        SyncTypeOptions();
    }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial AccountType Type { get; set; } = AccountType.Bank;
    [ObservableProperty] public partial string Currency { get; set; } = "MYR";
    [ObservableProperty] public partial string StartingBalanceText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool HasError => ErrorMessage.Length > 0;

    public IReadOnlyList<AccountTypeOption> TypeOptions { get; }

    public IReadOnlyList<string> SupportedCurrencies { get; }

    partial void OnTypeChanged(AccountType value) => SyncTypeOptions();

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_loaded)
            return;
        _loaded = true;

        Currency = (await _settings.GetAsync()).BaseCurrency;
    }

    [RelayCommand]
    private void SelectType(AccountTypeOption option) => Type = option.Type;

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Please give this account a name.";
            return;
        }

        var balance = 0m;
        if (!string.IsNullOrWhiteSpace(StartingBalanceText)
            && !decimal.TryParse(StartingBalanceText, NumberStyles.Number, CultureInfo.CurrentCulture, out balance))
        {
            ErrorMessage = "Enter the starting balance as a number.";
            return;
        }

        await _accounts.SaveAsync(new Account
        {
            Name = Name.Trim(),
            Type = Type,
            Currency = Currency,
            Balance = balance,
            ColorHex = Type switch
            {
                AccountType.Bank => "#5EC8FF",
                AccountType.Cash => "#22D3A2",
                AccountType.EWallet => "#FFC55E",
                AccountType.Investment => "#5EEAD4",
                _ => "#8F98A7"
            }
        });

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task Cancel() => Shell.Current.GoToAsync("..");

    private void SyncTypeOptions()
    {
        foreach (var option in TypeOptions)
            option.IsSelected = option.Type == Type;
    }
}
