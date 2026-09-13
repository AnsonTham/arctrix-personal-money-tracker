using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private const int TrendMonths = 6;
    private const int SpendingRows = 5;
    private const int RecentCount = 6;

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
    [ObservableProperty] public partial string TodayLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial string MonthLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";
    [ObservableProperty] public partial decimal NetWorth { get; set; }
    [ObservableProperty] public partial decimal CashBalance { get; set; }
    [ObservableProperty] public partial decimal InvestmentBalance { get; set; }
    [ObservableProperty] public partial decimal MonthlyIncome { get; set; }
    [ObservableProperty] public partial decimal MonthlyExpense { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetWorthChangeArrow))]
    [NotifyPropertyChangedFor(nameof(NetWorthChangeLabel))]
    public partial decimal NetWorthChange { get; set; }

    [ObservableProperty] public partial IReadOnlyList<ChartPoint> NetWorthTrend { get; set; } = [];
    [ObservableProperty] public partial bool ShowRecentEmptyState { get; set; }
    [ObservableProperty] public partial bool ShowSpendingEmptyState { get; set; }

    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<TransactionRowViewModel> RecentTransactions { get; } = new();
    public ObservableCollection<CategorySpend> TopSpending { get; } = new();

    /// <summary>Direction glyph paired with the change label, so direction never relies on color alone.</summary>
    public string NetWorthChangeArrow => NetWorthChange > 0 ? "▲" : NetWorthChange < 0 ? "▼" : "•";

    public string NetWorthChangeLabel => NetWorthChange == 0
        ? "No change this month"
        : $"{(NetWorthChange > 0 ? "+" : "−")}{BaseCurrency} {Math.Abs(NetWorthChange):N2} this month";

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await _recurring.RunDuePaymentsAsync();

            var now = DateTime.Now;
            var settings = await _settings.GetAsync();
            BaseCurrency = settings.BaseCurrency;
            Greeting = TimeOfDayGreeting(now);
            TodayLabel = now.ToString("dddd, d MMMM");
            MonthLabel = now.ToString("MMMM");

            var accounts = await _accounts.GetAllAsync();
            Accounts.Clear();
            foreach (var a in accounts) Accounts.Add(a);

            CashBalance = accounts.Where(a => a.Type is AccountType.Bank or AccountType.Cash or AccountType.EWallet).Sum(a => a.Balance);
            InvestmentBalance = accounts.Where(a => a.Type == AccountType.Investment).Sum(a => a.Balance);
            NetWorth = accounts.Sum(a => a.Balance);

            var flows = await _transactions.GetMonthlyFlowsAsync(now, TrendMonths);
            var thisMonth = flows[^1];
            MonthlyIncome = thisMonth.Income;
            MonthlyExpense = thisMonth.Expense;
            NetWorthChange = thisMonth.NetChange;
            NetWorthTrend = BuildNetWorthTrend(NetWorth, flows);

            TopSpending.Clear();
            foreach (var c in FoldTail(await _transactions.GetCategorySpendAsync(now.Year, now.Month), SpendingRows))
                TopSpending.Add(c);
            ShowSpendingEmptyState = TopSpending.Count == 0;

            var recent = await _transactions.GetRecentAsync(RecentCount);
            var categories = (await _categories.GetAllAsync(includeArchived: true)).ToDictionary(c => c.Id);
            var accountsById = accounts.ToDictionary(a => a.Id);

            RecentTransactions.Clear();
            foreach (var t in recent)
            {
                categories.TryGetValue(t.CategoryId, out var category);
                accountsById.TryGetValue(t.AccountId, out var account);
                RecentTransactions.Add(TransactionRowViewModel.From(t, category, account, BaseCurrency));
            }
            ShowRecentEmptyState = RecentTransactions.Count == 0;
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

    [RelayCommand]
    private Task ViewAllTransactions() => AppNavigation.GoToSectionAsync(Routes.History);

    [RelayCommand]
    private Task ViewAnalytics() => AppNavigation.GoToSectionAsync(Routes.Analytics);

    [RelayCommand]
    private Task OpenTransaction(TransactionRowViewModel row) =>
        Shell.Current.GoToAsync($"{Routes.AddTransaction}?{Routes.TransactionIdParam}={row.Id}");

    private static Task GoToAddTransaction(TransactionType type) =>
        Shell.Current.GoToAsync($"{Routes.AddTransaction}?{Routes.TransactionTypeParam}={type}");

    /// <summary>
    /// There is no balance history table, so month-end totals are reconstructed by walking
    /// back from today's total: each earlier month ends at the next month's end minus that
    /// month's net change.
    /// </summary>
    private static IReadOnlyList<ChartPoint> BuildNetWorthTrend(decimal currentTotal, IReadOnlyList<MonthlyFlow> flows)
    {
        var points = new ChartPoint[flows.Count];
        var monthEnd = currentTotal;
        for (var i = flows.Count - 1; i >= 0; i--)
        {
            points[i] = new ChartPoint(flows[i].MonthStart.ToString("MMM"), (double)monthEnd);
            monthEnd -= flows[i].NetChange;
        }
        return points;
    }

    /// <summary>Keeps the largest categories and folds the rest into a single "Other" row.</summary>
    private static IEnumerable<CategorySpend> FoldTail(IReadOnlyList<CategorySpend> spending, int maxRows)
    {
        if (spending.Count <= maxRows)
            return spending;

        var tail = spending.Skip(maxRows - 1).ToList();
        return spending.Take(maxRows - 1).Append(new CategorySpend
        {
            Name = "Other",
            Icon = "•",
            Amount = tail.Sum(c => c.Amount),
            PercentOfTotal = tail.Sum(c => c.PercentOfTotal)
        });
    }

    private static string TimeOfDayGreeting(DateTime now) => now.Hour switch
    {
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening"
    };
}
