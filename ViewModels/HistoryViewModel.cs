using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly IAccountService _accounts;
    private readonly ISettingsService _settings;

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
        Title = "History";
    }

    public ObservableCollection<TransactionRowViewModel> Transactions { get; } = new();

    [ObservableProperty] public partial bool IsEmpty { get; set; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await _settings.GetAsync();
            var all = await _transactions.GetAllAsync();
            var categories = await _categories.GetAllAsync(includeArchived: true);
            var accounts = await _accounts.GetAllAsync(includeArchived: true);

            Transactions.Clear();
            foreach (var t in all)
            {
                var category = categories.FirstOrDefault(c => c.Id == t.CategoryId);
                var account = accounts.FirstOrDefault(a => a.Id == t.AccountId);
                Transactions.Add(new TransactionRowViewModel
                {
                    Id = t.Id,
                    Type = t.Type,
                    CategoryName = category?.Name ?? "Others",
                    CategoryIcon = category?.Icon ?? "•",
                    AccountName = account?.Name ?? "",
                    Date = t.Date,
                    BaseAmount = t.BaseAmount,
                    Notes = t.Notes,
                    BaseCurrency = settings.BaseCurrency
                });
            }

            IsEmpty = Transactions.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenTransaction(TransactionRowViewModel row) =>
        Shell.Current.GoToAsync($"{Routes.AddTransaction}?{Routes.TransactionIdParam}={row.Id}");
}
