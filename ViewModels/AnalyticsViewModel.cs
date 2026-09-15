using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>
/// Period analytics. Every amount here is a historical total, so it stays in the base currency
/// each transaction was recorded in: the selected month is shown in its primary recorded
/// currency, and anything recorded under another base currency is listed alongside, not converted.
/// </summary>
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

    [ObservableProperty] public partial string MonthLabel { get; set; } = string.Empty;

    /// <summary>The recorded base currency the selected month, its KPIs and the chart are shown in.</summary>
    [ObservableProperty] public partial string ChartCurrency { get; set; } = "MYR";

    [ObservableProperty] public partial string IncomeText { get; set; } = "0.00";
    [ObservableProperty] public partial string IncomeCaption { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExpenseText { get; set; } = "0.00";
    [ObservableProperty] public partial string ExpenseCaption { get; set; } = string.Empty;
    [ObservableProperty] public partial string SavedCaption { get; set; } = "Saved";
    [ObservableProperty] public partial string SavedText { get; set; } = "0.00";
    [ObservableProperty] public partial string SavedDetail { get; set; } = string.Empty;
    [ObservableProperty] public partial string SavingsRateLabel { get; set; } = "—";

    /// <summary>One flow per month in <see cref="ChartCurrency"/>, for the chart.</summary>
    [ObservableProperty] public partial IReadOnlyList<MonthlyFlow> ChartFlows { get; set; } = [];

    /// <summary>Every month's flows in every recorded currency, for the table view.</summary>
    [ObservableProperty] public partial IReadOnlyList<MonthlyFlow> TableRows { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChartNote))]
    public partial string ChartNote { get; set; } = string.Empty;

    [ObservableProperty] public partial bool ShowSpendingEmptyState { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextMonthCommand))]
    public partial bool CanGoNext { get; set; }

    public ObservableCollection<CategorySpend> SpendByCategory { get; } = new();

    public bool HasChartNote => ChartNote.Length > 0;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var baseCurrency = (await _settings.GetAsync()).BaseCurrency;
            MonthLabel = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            CanGoNext = _month < FirstOfMonth(DateTime.Today);

            var flows = await _transactions.GetMonthlyFlowsAsync(_month, FlowMonths);
            var months = Enumerable.Range(1 - FlowMonths, FlowMonths).Select(_month.AddMonths).ToList();
            var selected = flows.Where(f => f.MonthStart == _month).ToList();
            var currency = MoneySummary.PickPrimary(selected.Select(f => f.Currency), baseCurrency);
            ChartCurrency = currency;

            var income = new MoneySummary(selected.Select(f => new CurrencyAmount(f.Currency, f.Income)), currency);
            var expense = new MoneySummary(selected.Select(f => new CurrencyAmount(f.Currency, f.Expense)), currency);
            var saved = new MoneySummary(selected.Select(f => new CurrencyAmount(f.Currency, f.Saved)), currency);
            IncomeText = income.Primary.Amount.ToString("N2", CultureInfo.CurrentCulture);
            IncomeCaption = Detail(income);
            ExpenseText = expense.Primary.Amount.ToString("N2", CultureInfo.CurrentCulture);
            ExpenseCaption = Detail(expense);
            // The caption carries the sign ("Overspent"), so the figure is shown unsigned.
            SavedCaption = saved.Primary.Amount >= 0 ? "Saved" : "Overspent";
            SavedText = Math.Abs(saved.Primary.Amount).ToString("N2", CultureInfo.CurrentCulture);
            SavedDetail = saved.HasOthers
                ? $"{saved.Primary.Currency} · also {string.Join(", ", saved.Others.Select(o => $"{(o.Amount < 0 ? "overspent" : "saved")} {o.Currency} {Math.Abs(o.Amount):N2}"))}"
                : saved.Primary.Currency;
            SavingsRateLabel = income.Primary.Amount > 0
                ? (saved.Primary.Amount / income.Primary.Amount).ToString("P0", CultureInfo.CurrentCulture)
                : "—";

            ChartFlows = months
                .Select(m => flows.FirstOrDefault(f => f.MonthStart == m && f.Currency == currency)
                             ?? new MonthlyFlow(m.Year, m.Month, currency, 0, 0))
                .ToList();

            TableRows = months
                .SelectMany(m =>
                {
                    var inMonth = flows.Where(f => f.MonthStart == m).OrderBy(f => f.Currency != currency).ThenBy(f => f.Currency).ToList();
                    return inMonth.Count > 0 ? inMonth : new List<MonthlyFlow> { new(m.Year, m.Month, currency, 0, 0) };
                })
                .ToList();

            var notPlotted = flows.Where(f => f.Currency != currency).Select(f => $"{f.MonthStart:MMM} ({f.Currency})").Distinct().ToList();
            ChartNote = notPlotted.Count == 0
                ? string.Empty
                : $"Not plotted: {string.Join(", ", notPlotted)}, recorded under a different base currency. See the breakdown below.";

            SpendByCategory.Clear();
            var spending = await _transactions.GetCategorySpendAsync(_month.Year, _month.Month);
            foreach (var c in spending.OrderBy(c => c.Currency != currency).ThenByDescending(c => c.Amount))
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

    /// <summary>"MYR", or "MYR, plus USD 10.00" when other recorded currencies are present.</summary>
    private static string Detail(MoneySummary summary) => summary.HasOthers
        ? $"{summary.Primary.Currency}, plus {summary.OthersLabel}"
        : summary.Primary.Currency;

    private static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);
}
