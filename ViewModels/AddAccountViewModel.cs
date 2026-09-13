using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class AddAccountViewModel : ViewModelBase
{
    private readonly IAccountService _accounts;

    public AddAccountViewModel(IAccountService accounts)
    {
        _accounts = accounts;
        Title = "Add Account";
    }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial AccountType Type { get; set; } = AccountType.Bank;
    [ObservableProperty] public partial string Currency { get; set; } = "MYR";
    [ObservableProperty] public partial decimal StartingBalance { get; set; }
    [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;

    public AccountType[] AccountTypes { get; } = Enum.GetValues<AccountType>();

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Please give this account a name.";
            return;
        }

        await _accounts.SaveAsync(new Account
        {
            Name = Name.Trim(),
            Type = Type,
            Currency = string.IsNullOrWhiteSpace(Currency) ? "MYR" : Currency.ToUpperInvariant(),
            Balance = StartingBalance,
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
}
