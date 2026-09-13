using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IAccountService _accounts;
    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly ISettingsService _settings;
    private readonly IRecurringPaymentService _recurring;

    public DashboardViewModel(
        IAccountService accounts,
        ITransactionService transactions,
        ICategoryService categories,
        ISettingsService settings,
        IRecurringPaymentService recurring)
    {
        _accounts = accounts;
        _transactions = transactions;
        _categories = categories;
        _settings = settings;
        _recurring = recurring;
        Title = "Dashboard";
    }

    [ObservableProperty] public partial string Greeting { get; set; } = "Good day";
    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";
    [ObservableProperty] public partial decimal NetWorth { get; set; }
    [ObservableProperty] public partial decimal CashBalance { get; set; }
    [ObservableProperty] public partial decimal InvestmentBalance { get; set; }
    [ObservableProperty] public partial decimal MonthlyIncome { get; set; }
    [ObservableProperty] public partial decimal MonthlyExpense { get; set; }

    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<TransactionRowViewModel> RecentTransactions { get; } = new();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await _recurring.RunDuePaymentsAsync();

            var settings = await _settings.GetAsync();
            BaseCurrency = settings.BaseCurrency;
            Greeting = TimeOfDayGreeting();

            var accounts = await _accounts.GetAllAsync();
            Accounts.Clear();
            foreach (var a in accounts) Accounts.Add(a);

            CashBalance = accounts.Where(a => a.Type is AccountType.Bank or AccountType.Cash or AccountType.EWallet).Sum(a => a.Balance);
            InvestmentBalance = accounts.Where(a => a.Type == AccountType.Investment).Sum(a => a.Balance);
            NetWorth = accounts.Sum(a => a.Balance);

            var now = DateTime.Now;
            MonthlyIncome = await _transactions.GetMonthlyTotalAsync(TransactionType.Income, now.Year, now.Month);
            MonthlyExpense = await _transactions.GetMonthlyTotalAsync(TransactionType.Expense, now.Year, now.Month);

            var recent = await _transactions.GetRecentAsync(6);
            var categories = await _categories.GetAllAsync(includeArchived: true);
            var accountsById = accounts.ToDictionary(a => a.Id);

            RecentTransactions.Clear();
            foreach (var t in recent)
            {
                var category = categories.FirstOrDefault(c => c.Id == t.CategoryId);
                accountsById.TryGetValue(t.AccountId, out var account);
                RecentTransactions.Add(new TransactionRowViewModel
                {
                    Id = t.Id,
                    Type = t.Type,
                    CategoryName = category?.Name ?? "Others",
                    CategoryIcon = category?.Icon ?? "•",
                    AccountName = account?.Name ?? "",
                    Date = t.Date,
                    BaseAmount = t.BaseAmount,
                    Notes = t.Notes,
                    BaseCurrency = BaseCurrency
                });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task QuickAddExpense() => GoToAddTransaction(TransactionType.Expense);

    [RelayCommand]
    private Task QuickAddIncome() => GoToAddTransaction(TransactionType.Income);

    [RelayCommand]
    private Task QuickAddTransfer() => GoToAddTransaction(TransactionType.Transfer);

    [RelayCommand]
    private Task QuickAddAccount() => Shell.Current.GoToAsync(Routes.AddAccount);

    private static Task GoToAddTransaction(TransactionType type) =>
        Shell.Current.GoToAsync($"{Routes.AddTransaction}?{Routes.TransactionTypeParam}={type}");

    private static string TimeOfDayGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour switch
        {
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening"
        };
    }
}
