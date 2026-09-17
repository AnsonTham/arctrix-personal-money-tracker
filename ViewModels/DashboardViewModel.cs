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

    // Current state: converted live into today's base currency.
    [ObservableProperty] public partial decimal NetWorth { get; set; }
    [ObservableProperty] public partial decimal CashBalance { get; set; }
    [ObservableProperty] public partial decimal InvestmentBalance { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetWorthChangeArrow))]
    [NotifyPropertyChangedFor(nameof(NetWorthChangeLabel))]
    public partial decimal NetWorthChange { get; set; }

    [ObservableProperty] public partial IReadOnlyList<ChartPoint> NetWorthTrend { get; set; } = [];

    // This month's flows: in the base currency each transaction was recorded in.
    [ObservableProperty] public partial string IncomeText { get; set; } = "0.00";
    [ObservableProperty] public partial string IncomeCaption { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExpenseText { get; set; } = "0.00";
    [ObservableProperty] public partial string ExpenseCaption { get; set; } = string.Empty;
    [ObservableProperty] public partial string SpendingCaption { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSpendingOthers))]
    public partial string SpendingOthersNote { get; set; } = string.Empty;

    [ObservableProperty] public partial bool ShowRecentEmptyState { get; set; }
    [ObservableProperty] public partial bool ShowSpendingEmptyState { get; set; }

    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<TransactionRowViewModel> RecentTransactions { get; } = new();
    public ObservableCollection<CategorySpend> TopSpending { get; } = new();

    public bool HasSpendingOthers => SpendingOthersNote.Length > 0;

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

            // Each account holds its own currency; convert before adding them up.
            decimal InBase(Account a) => _accounts.BalanceIn(a, BaseCurrency);
            CashBalance = accounts.Where(a => a.Type is AccountType.Bank or AccountType.Cash or AccountType.EWallet).Sum(a => InBase(a));
            InvestmentBalance = accounts.Where(a => a.Type == AccountType.Investment).Sum(a => InBase(a));
            NetWorth = accounts.Sum(a => InBase(a));

            var monthEnds = await _transactions.GetMonthEndNetWorthAsync(now, TrendMonths, BaseCurrency);
            NetWorthTrend = monthEnds.Select(m => new ChartPoint(m.MonthStart.ToString("MMM"), (double)m.Total)).ToList();
            NetWorthChange = monthEnds.Count > 1 ? monthEnds[^1].Total - monthEnds[^2].Total : 0;

            var flows = await _transactions.GetMonthlyFlowsAsync(now, 1);
            var monthCurrency = MoneySummary.PickPrimary(flows.Select(f => f.Currency), BaseCurrency);
            var income = new MoneySummary(flows.Select(f => new CurrencyAmount(f.Currency, f.Income)), monthCurrency);
            var expense = new MoneySummary(flows.Select(f => new CurrencyAmount(f.Currency, f.Expense)), monthCurrency);
            IncomeText = income.Primary.Amount.ToString("N2");
            IncomeCaption = Caption(income);
            ExpenseText = expense.Primary.Amount.ToString("N2");
            ExpenseCaption = Caption(expense);

            var spending = await _transactions.GetCategorySpendAsync(now.Year, now.Month);
            TopSpending.Clear();
            foreach (var c in FoldTail(spending.Where(c => c.Currency == monthCurrency).ToList(), SpendingRows, monthCurrency))
                TopSpending.Add(c);
            SpendingCaption = $"{MonthLabel} · {monthCurrency}";
            SpendingOthersNote = expense.HasOthers
                ? $"Also {expense.OthersLabel}, recorded under a different base currency."
                : string.Empty;
            ShowSpendingEmptyState = TopSpending.Count == 0;

            var recent = await _transactions.GetRecentAsync(RecentCount);
            var categories = (await _categories.GetAllAsync(includeArchived: true)).ToDictionary(c => c.Id);
            // Include archived accounts: recent rows may still name them.
            var accountsById = (await _accounts.GetAllAsync(includeArchived: true)).ToDictionary(a => a.Id);

            RecentTransactions.Clear();
            foreach (var t in recent)
            {
                categories.TryGetValue(t.CategoryId, out var category);
                accountsById.TryGetValue(t.AccountId, out var account);
                var toAccount = t.ToAccountId is int toId ? accountsById.GetValueOrDefault(toId) : null;
                RecentTransactions.Add(TransactionRowViewModel.From(t, category, account, toAccount, BaseCurrency));
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
    private Task ScanReceipt() =>
        ReceiptScanning.IsSupported ? Shell.Current.GoToAsync(Routes.ScanReceipt) : Task.CompletedTask;

    [RelayCommand]
    private Task ViewAllTransactions() => AppNavigation.GoToSectionAsync(Routes.History);

    [RelayCommand]
    private Task ViewAnalytics() => AppNavigation.GoToSectionAsync(Routes.Analytics);

    [RelayCommand]
    private Task OpenTransaction(TransactionRowViewModel row) =>
        Shell.Current.GoToAsync($"{Routes.AddTransaction}?{Routes.TransactionIdParam}={row.Id}");

    private static Task GoToAddTransaction(TransactionType type) =>
        Shell.Current.GoToAsync($"{Routes.AddTransaction}?{Routes.TransactionTypeParam}={type}");

    /// <summary>"September · MYR", plus any amounts recorded under other base currencies.</summary>
    private string Caption(MoneySummary summary) => summary.HasOthers
        ? $"{MonthLabel} · {summary.Primary.Currency}, plus {summary.OthersLabel}"
        : $"{MonthLabel} · {summary.Primary.Currency}";

    /// <summary>Keeps the largest categories and folds the rest into a single "Other" row.</summary>
    private static IEnumerable<CategorySpend> FoldTail(IReadOnlyList<CategorySpend> spending, int maxRows, string currency)
    {
        if (spending.Count <= maxRows)
            return spending;

        var tail = spending.Skip(maxRows - 1).ToList();
        return spending.Take(maxRows - 1).Append(new CategorySpend
        {
            Name = "Other",
            Icon = CategoryIcons.Other,
            Currency = currency,
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
