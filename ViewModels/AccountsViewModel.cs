using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class AccountsViewModel : ViewModelBase
{
    private readonly IAccountService _accounts;

    public AccountsViewModel(IAccountService accounts)
    {
        _accounts = accounts;
        Title = "Accounts";
    }

    [ObservableProperty] public partial decimal TotalBalance { get; set; }

    public ObservableCollection<Account> Accounts { get; } = new();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var all = await _accounts.GetAllAsync();
            Accounts.Clear();
            foreach (var a in all) Accounts.Add(a);
            TotalBalance = all.Sum(a => a.Balance);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task AddAccount() => Shell.Current.GoToAsync(Routes.AddAccount);
}
