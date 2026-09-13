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
    private readonly ISettingsService _settings;

    public AccountsViewModel(IAccountService accounts, ISettingsService settings)
    {
        _accounts = accounts;
        _settings = settings;
        Title = "Accounts";
    }

    [ObservableProperty] public partial decimal TotalBalance { get; set; }
    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";
    [ObservableProperty] public partial string AccountCountLabel { get; set; } = string.Empty;

    public ObservableCollection<Account> Accounts { get; } = new();

    /// <summary>Balance held per account type, largest first.</summary>
    public ObservableCollection<BalanceShare> Breakdown { get; } = new();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            BaseCurrency = (await _settings.GetAsync()).BaseCurrency;

            var all = await _accounts.GetAllAsync();
            Accounts.Clear();
            foreach (var a in all) Accounts.Add(a);

            TotalBalance = all.Sum(a => a.Balance);
            AccountCountLabel = all.Count == 1 ? "1 active account" : $"{all.Count} active accounts";

            Breakdown.Clear();
            foreach (var group in all.GroupBy(a => a.Type).OrderByDescending(g => g.Sum(a => a.Balance)))
            {
                var amount = group.Sum(a => a.Balance);
                var share = TotalBalance <= 0 ? 0 : (double)(Math.Max(amount, 0) / TotalBalance);
                Breakdown.Add(new BalanceShare(AccountTypeLabelConverter.Label(group.Key), amount, share));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task AddAccount() => Shell.Current.GoToAsync(Routes.AddAccount);

    [RelayCommand]
    private async Task Archive(Account account)
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            $"Archive {account.Name}?",
            "It will be hidden from your accounts and totals. Its past transactions are kept.",
            "Archive",
            "Cancel");
        if (!confirmed)
            return;

        await _accounts.ArchiveAsync(account.Id);
        await LoadAsync();
    }
}
