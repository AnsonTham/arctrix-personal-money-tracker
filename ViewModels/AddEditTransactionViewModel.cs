using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

[QueryProperty(nameof(TypeParam), Routes.TransactionTypeParam)]
[QueryProperty(nameof(IdParam), Routes.TransactionIdParam)]
public partial class AddEditTransactionViewModel : ViewModelBase
{
    private readonly ITransactionService _transactions;
    private readonly IAccountService _accounts;
    private readonly ICategoryService _categories;
    private readonly ISettingsService _settings;
    private readonly ICurrencyService _currency;

    private TransactionRecord? _editing;
    private string _baseCurrency = "MYR";

    public AddEditTransactionViewModel(
        ITransactionService transactions,
        IAccountService accounts,
        ICategoryService categories,
        ISettingsService settings,
        ICurrencyService currency)
    {
        _transactions = transactions;
        _accounts = accounts;
        _categories = categories;
        _settings = settings;
        _currency = currency;
    }

    // Navigation query params (string, since Shell passes strings)
    public string? TypeParam
    {
        set
        {
            if (Enum.TryParse<TransactionType>(value, out var parsed))
                SelectedType = parsed;
        }
    }

    public string? IdParam
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _ = LoadForEditAsync(id);
        }
    }

    [ObservableProperty] public partial TransactionType SelectedType { get; set; } = TransactionType.Expense;
    [ObservableProperty] public partial decimal Amount { get; set; }
    [ObservableProperty] public partial string CurrencyCode { get; set; } = "MYR";
    [ObservableProperty] public partial DateTime Date { get; set; } = DateTime.Now;
    [ObservableProperty] public partial string Notes { get; set; } = string.Empty;
    [ObservableProperty] public partial Account? SelectedAccount { get; set; }
    [ObservableProperty] public partial Account? SelectedToAccount { get; set; }
    [ObservableProperty] public partial Category? SelectedCategory { get; set; }
    [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsEditing { get; set; }

    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public TransactionType[] TransactionTypes { get; } = Enum.GetValues<TransactionType>();
    public IReadOnlyList<string> SupportedCurrencies => _currency.SupportedCurrencies;

    public bool ShowToAccount => SelectedType is TransactionType.Transfer or TransactionType.Investment;

    partial void OnSelectedTypeChanged(TransactionType value) => OnPropertyChanged(nameof(ShowToAccount));

    [RelayCommand]
    public async Task LoadAsync()
    {
        var settings = await _settings.GetAsync();
        _baseCurrency = settings.BaseCurrency;
        CurrencyCode = _baseCurrency;

        var accounts = await _accounts.GetAllAsync();
        Accounts.Clear();
        foreach (var a in accounts) Accounts.Add(a);
        SelectedAccount ??= Accounts.FirstOrDefault();

        var categories = await _categories.GetAllAsync();
        Categories.Clear();
        foreach (var c in categories.Where(c => c.DefaultType == SelectedType || !c.IsSystem)) Categories.Add(c);
        SelectedCategory ??= Categories.FirstOrDefault();
    }

    private async Task LoadForEditAsync(int id)
    {
        _editing = await _transactions.GetByIdAsync(id);
        if (_editing is null) return;

        IsEditing = true;
        Title = "Edit Transaction";
        SelectedType = _editing.Type;
        Amount = _editing.OriginalAmount;
        CurrencyCode = _editing.OriginalCurrency;
        Date = _editing.Date;
        Notes = _editing.Notes;

        await LoadAsync();
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == _editing.AccountId);
        SelectedToAccount = Accounts.FirstOrDefault(a => a.Id == _editing.ToAccountId);
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == _editing.CategoryId) ??
                           (await _categories.GetByIdAsync(_editing.CategoryId));
    }

    [RelayCommand]
    private async Task Save()
    {
        if (Amount <= 0)
        {
            ErrorMessage = "Enter an amount greater than zero.";
            return;
        }
        if (SelectedAccount is null)
        {
            ErrorMessage = "Choose an account.";
            return;
        }
        if (SelectedCategory is null)
        {
            ErrorMessage = "Choose a category.";
            return;
        }
        if (ShowToAccount && SelectedToAccount is null)
        {
            ErrorMessage = "Choose a destination account.";
            return;
        }
        if (ShowToAccount && SelectedToAccount?.Id == SelectedAccount.Id)
        {
            ErrorMessage = "Source and destination accounts must be different.";
            return;
        }

        var rate = _currency.GetRate(CurrencyCode, _baseCurrency);

        var record = new TransactionRecord
        {
            Id = _editing?.Id ?? 0,
            Type = SelectedType,
            AccountId = SelectedAccount.Id,
            ToAccountId = ShowToAccount ? SelectedToAccount!.Id : null,
            CategoryId = SelectedCategory.Id,
            OriginalAmount = Amount,
            OriginalCurrency = CurrencyCode,
            ExchangeRate = rate,
            BaseAmount = Amount * rate,
            Date = Date,
            Notes = Notes.Trim(),
            CreatedAt = _editing?.CreatedAt ?? DateTime.Now
        };

        if (_editing is null)
            await _transactions.AddAsync(record);
        else
            await _transactions.UpdateAsync(_editing, record);

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (_editing is not null)
        {
            await _transactions.DeleteAsync(_editing);
        }
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task Cancel() => Shell.Current.GoToAsync("..");
}
