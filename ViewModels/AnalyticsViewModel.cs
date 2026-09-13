using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class AnalyticsViewModel : ViewModelBase
{
    private const int FlowMonths = 6;

    private readonly ITransactionService _transactions;
    private readonly ISettingsService _settings;

    private DateTime _month = FirstOfMonth(DateTime.Today);

    public AnalyticsViewModel(ITransactionService transactions, ISettingsService settings)
    {
        _transactions = transactions;
        _settings = settings;
        Title = "Analytics";
    }

    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";
    [ObservableProperty] public partial string MonthLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal TotalIncome { get; set; }
    [ObservableProperty] public partial decimal TotalExpense { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavedCaption))]
    public partial decimal NetAmount { get; set; }

    [ObservableProperty] public partial string SavingsRateLabel { get; set; } = "—";
    [ObservableProperty] public partial IReadOnlyList<MonthlyFlow> Flows { get; set; } = [];
    [ObservableProperty] public partial bool ShowSpendingEmptyState { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextMonthCommand))]
    public partial bool CanGoNext { get; set; }

    public ObservableCollection<CategorySpend> SpendByCategory { get; } = new();

    public string SavedCaption => NetAmount >= 0 ? "Saved" : "Overspent";

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            BaseCurrency = (await _settings.GetAsync()).BaseCurrency;
            MonthLabel = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            CanGoNext = _month < FirstOfMonth(DateTime.Today);

            Flows = await _transactions.GetMonthlyFlowsAsync(_month, FlowMonths);
            var selected = Flows[^1];
            TotalIncome = selected.Income;
            TotalExpense = selected.Expense;
            NetAmount = selected.Saved;
            SavingsRateLabel = selected.Income > 0
                ? (selected.Saved / selected.Income).ToString("P0", CultureInfo.CurrentCulture)
                : "—";

            SpendByCategory.Clear();
            foreach (var c in await _transactions.GetCategorySpendAsync(_month.Year, _month.Month))
                SpendByCategory.Add(c);
            ShowSpendingEmptyState = SpendByCategory.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task PreviousMonth()
    {
        _month = _month.AddMonths(-1);
        return LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextMonth()
    {
        _month = _month.AddMonths(1);
        return LoadAsync();
    }

    private static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);
}
