using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>
/// Rename an account, or correct its balance to what the bank actually says.
///
/// The name is a plain edit. The balance is not: typing a new figure posts a balance adjustment
/// through <see cref="ITransactionService"/> for the difference, exactly as any other transaction
/// is posted. The stored balance is never written directly here, so it always remains the sum of
/// the transactions behind it, and the correction shows up in history rather than happening
/// invisibly.
/// </summary>
public partial class EditAccountViewModel : ViewModelBase, IQueryAttributable
{
    private const string AdjustmentNote = "Manual balance correction";

    private readonly IAccountService _accounts;
    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly ISettingsService _settings;
    private readonly ICurrencyService _currency;

    private Account? _account;
    private int? _accountId;
    private bool _loaded;
    private string _baseCurrency = "MYR";

    public EditAccountViewModel(
        IAccountService accounts,
        ITransactionService transactions,
        ICategoryService categories,
        ISettingsService settings,
        ICurrencyService currency)
    {
        _accounts = accounts;
        _transactions = transactions;
        _categories = categories;
        _settings = settings;
        _currency = currency;
        Title = "Edit account";

        TypeOptions = Enum.GetValues<AccountType>().Select(t => new AccountTypeOption(t)).ToList();
    }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial AccountType Type { get; set; } = AccountType.Bank;
    [ObservableProperty] public partial string Currency { get; set; } = "MYR";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdjustmentPreview))]
    public partial string BalanceText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdjustmentPreview))]
    public partial decimal StoredBalance { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public IReadOnlyList<AccountTypeOption> TypeOptions { get; }

    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>Says what correcting the balance will record, before it is recorded.</summary>
    public string AdjustmentPreview
    {
        get
        {
            if (!TryReadBalance(out var target))
                return "Enter the balance as a number.";

            var difference = target - StoredBalance;
            if (difference == 0)
                return "Balance unchanged - nothing will be posted.";

            return difference > 0
                ? $"Posts a balance adjustment of + {Currency} {difference:N2} as income, dated today."
                : $"Posts a balance adjustment of - {Currency} {Math.Abs(difference):N2} as an expense, dated today.";
        }
    }

    partial void OnTypeChanged(AccountType value) => SyncTypeOptions();

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(Routes.AccountIdParam, out var id)
            && int.TryParse(id?.ToString(), out var parsed)
            && parsed > 0)
        {
            _accountId = parsed;
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_loaded)
            return;
        _loaded = true;

        _baseCurrency = (await _settings.GetAsync()).BaseCurrency;

        if (_accountId is not int id || await _accounts.GetByIdAsync(id) is not { } account)
        {
            ErrorMessage = "That account no longer exists.";
            return;
        }

        _account = account;
        Name = account.Name;
        Type = account.Type;
        Currency = account.Currency;
        StoredBalance = account.Balance;
        BalanceText = account.Balance.ToString("0.##", CultureInfo.CurrentCulture);
        SyncTypeOptions();
    }

    [RelayCommand]
    private void SelectType(AccountTypeOption option) => Type = option.Type;

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = string.Empty;

        if (_account is null)
            return;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Please give this account a name.";
            return;
        }
        if (!TryReadBalance(out var target))
        {
            ErrorMessage = "Enter the balance as a number.";
            return;
        }

        // Re-read: a recurring payment may have posted since this form was opened, and the
        // adjustment has to be the difference from what the account actually holds now.
        var current = await _accounts.GetByIdAsync(_account.Id) ?? _account;
        var difference = target - current.Balance;

        if (difference < 0 && target < 0)
        {
            var confirmed = await Shell.Current.DisplayAlertAsync(
                "Set a negative balance?",
                $"{Name.Trim()} would be left at {Currency} {target:N2}.",
                "Yes, it's overdrawn",
                "Go back");
            if (!confirmed)
                return;
        }

        // The name and type are the only fields written straight to the account; the balance on
        // this instance is the stored one, untouched.
        current.Name = Name.Trim();
        current.Type = Type;
        await _accounts.SaveAsync(current);

        if (difference != 0 && !await PostAdjustmentAsync(current, difference))
            return;

        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// Posts the difference as an ordinary transaction, which is what actually moves the balance.
    /// Returns false when it could not be posted, leaving the form open.
    /// </summary>
    private async Task<bool> PostAdjustmentAsync(Account account, decimal difference)
    {
        var type = difference > 0 ? TransactionType.Income : TransactionType.Expense;
        var amount = Math.Abs(difference);
        var category = await AdjustmentCategoryAsync(type);
        var rate = _currency.GetRate(account.Currency, _baseCurrency);

        var adjustment = new TransactionRecord
        {
            Type = type,
            AccountId = account.Id,
            CategoryId = category?.Id ?? 0,
            OriginalAmount = amount,
            OriginalCurrency = account.Currency,
            ExchangeRate = rate,
            BaseAmount = amount * rate,
            BaseCurrencyAtEntry = _baseCurrency,
            Date = DateTime.Today,
            Notes = AdjustmentNote,
            CreatedAt = DateTime.Now
        };

        try
        {
            // A correction states what the account really holds, so a negative result is allowed -
            // the user has already confirmed it above.
            await _transactions.AddAsync(adjustment, allowOverdraw: true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't record the balance adjustment: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// A correction is not spending on anything, so it goes to the neutral bucket rather than the
    /// first category that happens to match the type - filing it under Food would quietly distort
    /// the spending breakdown.
    /// </summary>
    private async Task<Category?> AdjustmentCategoryAsync(TransactionType type)
    {
        var preferred = type == TransactionType.Income ? "Income" : "Others";
        var categories = await _categories.GetAllAsync(includeArchived: true);

        return categories.FirstOrDefault(c => c.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase))
            ?? categories.FirstOrDefault(c => c.IsSystem && c.DefaultType == type)
            ?? categories.FirstOrDefault(c => c.DefaultType == type)
            ?? categories.FirstOrDefault();
    }

    [RelayCommand]
    private Task Cancel() => Shell.Current.GoToAsync("..");

    private bool TryReadBalance(out decimal balance) =>
        decimal.TryParse(BalanceText, NumberStyles.Number, CultureInfo.CurrentCulture, out balance);

    private void SyncTypeOptions()
    {
        foreach (var option in TypeOptions)
            option.IsSelected = option.Type == Type;
    }
}
