using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>
/// Add or edit a transaction. Query parameters: <see cref="Routes.TransactionTypeParam"/>
/// preselects the type for a new transaction; <see cref="Routes.TransactionIdParam"/> opens
/// an existing one for editing; <see cref="Routes.ReceiptScanParam"/> (a <see cref="ReceiptDraft"/>)
/// pre-fills a new one from a scanned receipt and attaches its photo.
/// </summary>
public partial class AddEditTransactionViewModel : ViewModelBase, IQueryAttributable
{
    private readonly ITransactionService _transactions;
    private readonly IAccountService _accounts;
    private readonly ICategoryService _categories;
    private readonly ISettingsService _settings;
    private readonly ICurrencyService _currency;
    private readonly IReceiptPhotoStore _receiptPhotos;

    private TransactionRecord? _editing;
    private int? _editId;
    private ReceiptDraft? _receiptDraft;
    private string? _receiptPath;
    private bool _loaded;
    private bool _saved;
    private string _baseCurrency = "MYR";
    private List<Category> _allCategories = new();

    public AddEditTransactionViewModel(
        ITransactionService transactions,
        IAccountService accounts,
        ICategoryService categories,
        ISettingsService settings,
        ICurrencyService currency,
        IReceiptPhotoStore receiptPhotos)
    {
        _transactions = transactions;
        _accounts = accounts;
        _categories = categories;
        _settings = settings;
        _currency = currency;
        _receiptPhotos = receiptPhotos;
        Title = "Add transaction";

        TypeOptions = Enum.GetValues<TransactionType>()
            .Select(t => new FilterOption(t.ToString(), t))
            .ToList();
        SyncTypeOptions();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowToAccount))]
    [NotifyPropertyChangedFor(nameof(AccountLabel))]
    public partial TransactionType SelectedType { get; set; } = TransactionType.Expense;

    [ObservableProperty] public partial string AmountText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CurrencyCode { get; set; } = "MYR";
    [ObservableProperty] public partial DateTime Date { get; set; } = DateTime.Now;
    [ObservableProperty] public partial string Notes { get; set; } = string.Empty;
    [ObservableProperty] public partial Account? SelectedAccount { get; set; }
    [ObservableProperty] public partial Account? SelectedToAccount { get; set; }
    [ObservableProperty] public partial Category? SelectedCategory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    [NotifyPropertyChangedFor(nameof(CanScanReceipt))]
    public partial bool IsEditing { get; set; }

    /// <summary>Full path of the attached receipt photo, for display; null when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReceiptPhoto))]
    [NotifyPropertyChangedFor(nameof(CanScanReceipt))]
    public partial string? ReceiptPhotoSource { get; set; }

    /// <summary>Shown above the form after a scan, so pre-filled values get checked before saving.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScanNotice))]
    public partial string ScanNotice { get; set; } = string.Empty;

    [ObservableProperty] public partial bool ScanFailed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScannedItems))]
    public partial IReadOnlyList<ReceiptLineItem> ScannedItems { get; set; } = [];

    public IReadOnlyList<FilterOption> TypeOptions { get; }
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = new();
    public ObservableCollection<Account> Accounts { get; } = new();
    public IReadOnlyList<string> SupportedCurrencies => _currency.SupportedCurrencies;

    public bool ShowToAccount => SelectedType is TransactionType.Transfer or TransactionType.Investment;
    public string AccountLabel => ShowToAccount ? "From account" : "Account";
    public bool HasError => ErrorMessage.Length > 0;
    public string SaveLabel => IsEditing ? "Save changes" : "Save transaction";
    public bool HasReceiptPhoto => ReceiptPhotoSource is not null;
    public bool HasScanNotice => ScanNotice.Length > 0;
    public bool HasScannedItems => ScannedItems.Count > 0;

    /// <summary>A scan starts a new transaction from a photo, so it's offered on mobile when adding one without a photo yet.</summary>
    public bool CanScanReceipt => ReceiptScanning.IsSupported && !IsEditing && !HasReceiptPhoto;

    partial void OnSelectedTypeChanged(TransactionType value)
    {
        SyncTypeOptions();
        RefreshCategories();
    }

    /// <summary>Shell delivers query parameters before the page appears, so LoadAsync sees them.</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(Routes.TransactionTypeParam, out var type)
            && Enum.TryParse<TransactionType>(type?.ToString(), out var parsedType))
        {
            SelectedType = parsedType;
        }

        if (query.TryGetValue(Routes.TransactionIdParam, out var id)
            && int.TryParse(id?.ToString(), out var parsedId)
            && parsedId > 0)
        {
            _editId = parsedId;
        }

        if (query.TryGetValue(Routes.ReceiptScanParam, out var draft) && draft is ReceiptDraft receiptDraft)
            _receiptDraft = receiptDraft;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_loaded)
            return;
        _loaded = true;

        var settings = await _settings.GetAsync();
        _baseCurrency = settings.BaseCurrency;

        Accounts.Clear();
        foreach (var a in await _accounts.GetAllAsync()) Accounts.Add(a);
        _allCategories = await _categories.GetAllAsync();

        if (_editId is int id && await _transactions.GetByIdAsync(id) is TransactionRecord existing)
        {
            _editing = existing;
            IsEditing = true;
            Title = "Edit transaction";
            AmountText = existing.OriginalAmount.ToString("0.##", CultureInfo.CurrentCulture);
            CurrencyCode = existing.OriginalCurrency;
            Date = existing.Date;
            Notes = existing.Notes;
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == existing.AccountId);
            SelectedToAccount = Accounts.FirstOrDefault(a => a.Id == existing.ToAccountId);

            // An older transaction may point at a category that has since been archived.
            if (_allCategories.All(c => c.Id != existing.CategoryId)
                && await _categories.GetByIdAsync(existing.CategoryId) is Category archived)
            {
                _allCategories.Add(archived);
            }

            SelectedCategory = _allCategories.FirstOrDefault(c => c.Id == existing.CategoryId);
            SelectedType = existing.Type;
            SetReceiptPhoto(existing.ReceiptImagePath);
        }
        else
        {
            CurrencyCode = _baseCurrency;
            SelectedAccount = Accounts.FirstOrDefault();

            if (_receiptDraft is not null)
                ApplyReceiptDraft(_receiptDraft);
        }

        RefreshCategories();
    }

    /// <summary>
    /// Pre-fills a new transaction from a scanned receipt. Every field stays editable and nothing is
    /// saved until the user taps Save; the notice says the values came from a scan.
    /// </summary>
    private void ApplyReceiptDraft(ReceiptDraft draft)
    {
        SetReceiptPhoto(draft.PhotoPath);

        if (draft.Scan is not ReceiptScanResult scan)
        {
            ScanFailed = true;
            ScanNotice = "Couldn't read the receipt — enter it manually. The photo is still attached.";
            return;
        }

        SelectedType = TransactionType.Expense;
        if (scan.Total is decimal total)
            AmountText = total.ToString("0.00", CultureInfo.CurrentCulture);
        if (scan.Date is DateTime date)
            Date = date.Date + DateTime.Now.TimeOfDay;
        if (scan.ShopName is string shopName)
            Notes = shopName;
        if (scan.SuggestedCategory is string categoryName
            && _allCategories.FirstOrDefault(c => c.DefaultType == TransactionType.Expense
                && string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase)) is Category suggested)
        {
            SelectedCategory = suggested;
        }

        ScannedItems = scan.Items;
        ScanNotice = "Scanned from your receipt — please check the details before saving.";
    }

    private void SetReceiptPhoto(string? storedPath)
    {
        _receiptPath = storedPath;
        ReceiptPhotoSource = storedPath is null ? null : _receiptPhotos.GetFullPath(storedPath);
    }

    /// <summary>Deletes a photo attached by a scan when the form is left without saving.</summary>
    public void DiscardUnsavedReceipt()
    {
        if (_saved || _editing is not null || _receiptPath is null)
            return;

        _receiptPhotos.Delete(_receiptPath);
        SetReceiptPhoto(null);
    }

    [RelayCommand]
    private void SelectType(FilterOption option)
    {
        if (option.Type is TransactionType type)
            SelectedType = type;
    }

    [RelayCommand]
    private void SelectCategory(CategoryOption option)
    {
        SelectedCategory = option.Category;
        foreach (var o in CategoryOptions)
            o.IsSelected = ReferenceEquals(o, option);
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = string.Empty;

        if (!decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
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
            ToAccountId = ShowToAccount ? SelectedToAccount?.Id : null,
            CategoryId = SelectedCategory.Id,
            OriginalAmount = amount,
            OriginalCurrency = CurrencyCode,
            ExchangeRate = rate,
            BaseAmount = amount * rate,
            BaseCurrencyAtEntry = _baseCurrency,
            Date = Date,
            Notes = Notes.Trim(),
            // Preserve the link to the recurring payment that generated this row, if any.
            RecurringPaymentId = _editing?.RecurringPaymentId,
            ReceiptImagePath = _receiptPath,
            CreatedAt = _editing?.CreatedAt ?? DateTime.Now
        };

        try
        {
            if (_editing is null)
                await _transactions.AddAsync(record);
            else
                await _transactions.UpdateAsync(_editing, record);
        }
        catch (Exception ex)
        {
            // The save is atomic, so nothing was written; keep the form open with its values.
            ErrorMessage = $"Couldn't save this transaction: {ex.Message}";
            return;
        }

        _saved = true;
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task ScanReceipt() =>
        CanScanReceipt ? Shell.Current.GoToAsync($"../{Routes.ScanReceipt}") : Task.CompletedTask;

    [RelayCommand]
    private Task ViewReceipt() =>
        ReceiptScanning.IsSupported && ReceiptPhotoSource is string path
            ? Shell.Current.GoToAsync(Routes.ReceiptPhoto, new ShellNavigationQueryParameters { [Routes.ReceiptPathParam] = path })
            : Task.CompletedTask;

    [RelayCommand]
    private async Task Delete()
    {
        if (_editing is null)
            return;

        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Delete this transaction?",
            "Its effect on your account balances will be reversed. This can't be undone.",
            "Delete",
            "Keep");
        if (!confirmed)
            return;

        try
        {
            await _transactions.DeleteAsync(_editing);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't delete this transaction: {ex.Message}";
            return;
        }

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task Cancel() => Shell.Current.GoToAsync("..");

    private void SyncTypeOptions()
    {
        foreach (var option in TypeOptions)
            option.IsSelected = option.Type == SelectedType;
    }

    /// <summary>Shows the categories meant for the selected type plus any user-created ones.</summary>
    private void RefreshCategories()
    {
        var visible = _allCategories
            .Where(c => c.DefaultType == SelectedType || !c.IsSystem)
            .ToList();

        if (SelectedCategory is Category selected && visible.All(c => c.Id != selected.Id))
        {
            // Keep the category an edited transaction already has, even if it's filed under another type.
            if (_editing?.CategoryId == selected.Id)
                visible.Add(selected);
            else
                SelectedCategory = null;
        }
        SelectedCategory ??= visible.FirstOrDefault();

        CategoryOptions.Clear();
        foreach (var category in visible)
            CategoryOptions.Add(new CategoryOption(category) { IsSelected = category.Id == SelectedCategory?.Id });
    }
}
