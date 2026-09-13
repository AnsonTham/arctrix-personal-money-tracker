using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public class CategorySpend
{
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = "•";
    public string ColorHex { get; init; } = "#8F98A7";
    public decimal Amount { get; init; }
    public double PercentOfTotal { get; init; }
}

public partial class AnalyticsViewModel : ViewModelBase
{
    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly ISettingsService _settings;

    public AnalyticsViewModel(ITransactionService transactions, ICategoryService categories, ISettingsService settings)
    {
        _transactions = transactions;
        _categories = categories;
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
            var monthTx = await _transactions.GetForMonthAsync(now.Year, now.Month);
            var categories = await _categories.GetAllAsync(includeArchived: true);

            TotalExpense = monthTx.Where(t => t.Type == TransactionType.Expense).Sum(t => t.BaseAmount);
            TotalIncome = monthTx.Where(t => t.Type == TransactionType.Income).Sum(t => t.BaseAmount);

            var grouped = monthTx
                .Where(t => t.Type == TransactionType.Expense)
                .GroupBy(t => t.CategoryId)
                .Select(g =>
                {
                    var cat = categories.FirstOrDefault(c => c.Id == g.Key);
                    var sum = g.Sum(t => t.BaseAmount);
                    return new CategorySpend
                    {
                        Name = cat?.Name ?? "Others",
                        Icon = cat?.Icon ?? "•",
                        ColorHex = cat?.ColorHex ?? "#8F98A7",
                        Amount = sum,
                        PercentOfTotal = TotalExpense == 0 ? 0 : (double)(sum / TotalExpense) * 100.0
                    };
                })
                .OrderByDescending(c => c.Amount);

            SpendByCategory.Clear();
            foreach (var c in grouped) SpendByCategory.Add(c);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
