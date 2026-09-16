using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly IAccountService _accounts;
    private readonly ISettingsService _settings;

    private List<TransactionRowViewModel> _all = new();
    private FilterOption _activeFilter;

    public HistoryViewModel(
        ITransactionService transactions,
        ICategoryService categories,
        IAccountService accounts,
        ISettingsService settings)
    {
        _transactions = transactions;
        _categories = categories;
        _accounts = accounts;
        _settings = settings;
        Title = "Transactions";

        Filters =
        [
            new FilterOption("All", null),
            new FilterOption("Expenses", TransactionType.Expense),
            new FilterOption("Income", TransactionType.Income),
            new FilterOption("Transfers", TransactionType.Transfer),
            new FilterOption("Investments", TransactionType.Investment)
        ];
        _activeFilter = Filters[0];
        _activeFilter.IsSelected = true;
    }

    public IReadOnlyList<FilterOption> Filters { get; }

    [ObservableProperty] public partial IReadOnlyList<TransactionGroup> Groups { get; set; } = [];
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";
    [ObservableProperty] public partial string ResultLabel { get; set; } = string.Empty;

    /// <summary>Totals for the current filter, per recorded base currency (e.g. "USD 10.00 + MYR 470.00").</summary>
    [ObservableProperty] public partial string IncomeLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExpenseLabel { get; set; } = string.Empty;

    [ObservableProperty] public partial string EmptyTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string EmptyMessage { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await _settings.GetAsync();
            BaseCurrency = settings.BaseCurrency;

            var records = await _transactions.GetAllAsync();
            var categories = (await _categories.GetAllAsync(includeArchived: true)).ToDictionary(c => c.Id);
            var accounts = (await _accounts.GetAllAsync(includeArchived: true)).ToDictionary(a => a.Id);

            _all = records
                .Select(t => TransactionRowViewModel.From(
                    t,
                    categories.GetValueOrDefault(t.CategoryId),
                    accounts.GetValueOrDefault(t.AccountId),
                    t.ToAccountId is int toId ? accounts.GetValueOrDefault(toId) : null,
                    BaseCurrency))
                .ToList();

            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectFilter(FilterOption option)
    {
        _activeFilter.IsSelected = false;
        _activeFilter = option;
        _activeFilter.IsSelected = true;
        ApplyFilter();
    }

    [RelayCommand]
    private Task OpenTransaction(TransactionRowViewModel row) =>
        Shell.Current.GoToAsync($"{Routes.AddTransaction}?{Routes.TransactionIdParam}={row.Id}");

    [RelayCommand]
    private Task AddTransaction() => Shell.Current.GoToAsync(Routes.AddTransaction);

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var rows = _all
            .Where(r => _activeFilter.Type is null || r.Type == _activeFilter.Type)
            .Where(r => query.Length == 0 || r.Matches(query))
            .ToList();

        // Rows arrive newest first, so day groups keep that order.
        Groups = rows
            .GroupBy(r => r.Date.Date)
            .Select(day => new TransactionGroup(day.Key, day))
            .ToList();

        var primary = MoneySummary.PickPrimary(rows.Select(r => r.BaseCurrency), BaseCurrency);
        IncomeLabel = Total(rows, TransactionType.Income, primary);
        ExpenseLabel = Total(rows, TransactionType.Expense, primary);
        ResultLabel = rows.Count == 1 ? "1 transaction" : $"{rows.Count:N0} transactions";

        (EmptyTitle, EmptyMessage) = _all.Count == 0
            ? ("No transactions yet", "Add your first income or expense to start tracking.")
            : ("No matches", "Try a different search or filter.");
    }

    private static string Total(IEnumerable<TransactionRowViewModel> rows, TransactionType type, string primaryCurrency) =>
        new MoneySummary(rows.Where(r => r.Type == type).Select(r => new CurrencyAmount(r.BaseCurrency, r.BaseAmount)), primaryCurrency).Label;
}
