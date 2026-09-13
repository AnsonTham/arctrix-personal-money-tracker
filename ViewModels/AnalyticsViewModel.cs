using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class AnalyticsViewModel : ViewModelBase
{
    private readonly ITransactionService _transactions;
    private readonly ISettingsService _settings;

    public AnalyticsViewModel(ITransactionService transactions, ISettingsService settings)
    {
        _transactions = transactions;
        _settings = settings;
        Title = "Analytics";
    }

    [ObservableProperty] public partial decimal TotalExpense { get; set; }
    [ObservableProperty] public partial decimal TotalIncome { get; set; }
    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";
    [ObservableProperty] public partial string MonthLabel { get; set; } = DateTime.Now.ToString("MMMM yyyy");

    public ObservableCollection<CategorySpend> SpendByCategory { get; } = new();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await _settings.GetAsync();
            BaseCurrency = settings.BaseCurrency;

            var now = DateTime.Now;
            TotalExpense = await _transactions.GetMonthlyTotalAsync(TransactionType.Expense, now.Year, now.Month);
            TotalIncome = await _transactions.GetMonthlyTotalAsync(TransactionType.Income, now.Year, now.Month);

            SpendByCategory.Clear();
            foreach (var c in await _transactions.GetCategorySpendAsync(now.Year, now.Month))
                SpendByCategory.Add(c);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
