using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class AddRecurringViewModel : ViewModelBase
{
    private readonly IRecurringPaymentService _recurring;
    private readonly IAccountService _accounts;
    private readonly ICategoryService _categories;
    private readonly ISettingsService _settings;

    private bool _loaded;
    private List<Category> _allCategories = new();

    public AddRecurringViewModel(
        IRecurringPaymentService recurring,
        IAccountService accounts,
        ICategoryService categories,
        ISettingsService settings)
    {
        _recurring = recurring;
        _accounts = accounts;
        _categories = categories;
        _settings = settings;
        Title = "Add recurring payment";

        TypeOptions =
        [
            new FilterOption("Expense", TransactionType.Expense),
            new FilterOption("Income", TransactionType.Income)
        ];
        SyncTypeOptions();
    }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial TransactionType Type { get; set; } = TransactionType.Expense;
    [ObservableProperty] public partial string AmountText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstPaymentLabel))]
    public partial int DayOfMonth { get; set; } = 1;

    [ObservableProperty] public partial Account? SelectedAccount { get; set; }
    [ObservableProperty] public partial Category? SelectedCategory { get; set; }
    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public IReadOnlyList<FilterOption> TypeOptions { get; }
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = new();
    public ObservableCollection<Account> Accounts { get; } = new();
    public int[] DaysOfMonth { get; } = Enumerable.Range(1, 31).ToArray();

    public bool HasError => ErrorMessage.Length > 0;

    public string FirstPaymentLabel => $"First posts on {NextDueDate(DayOfMonth):d MMM yyyy}, then every month.";

    partial void OnTypeChanged(TransactionType value)
    {
        SyncTypeOptions();
        RefreshCategories();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_loaded)
            return;
        _loaded = true;

        BaseCurrency = (await _settings.GetAsync()).BaseCurrency;

        Accounts.Clear();
        foreach (var a in await _accounts.GetAllAsync()) Accounts.Add(a);
        SelectedAccount = Accounts.FirstOrDefault();

        _allCategories = await _categories.GetAllAsync();
        RefreshCategories();
    }

    [RelayCommand]
    private void SelectType(FilterOption option)
    {
        if (option.Type is TransactionType type)
            Type = type;
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

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Give this payment a name.";
            return;
        }
        if (!decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            ErrorMessage = "Enter an amount greater than zero.";
            return;
        }
        if (SelectedAccount is null || SelectedCategory is null)
        {
            ErrorMessage = "Choose an account and category.";
            return;
        }

        await _recurring.SaveAsync(new RecurringPayment
        {
            Name = Name.Trim(),
            Type = Type,
            AccountId = SelectedAccount.Id,
            CategoryId = SelectedCategory.Id,
            Amount = amount,
            Currency = BaseCurrency,
            DayOfMonth = DayOfMonth,
            NextDueDate = NextDueDate(DayOfMonth),
            IsActive = true
        });

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task Cancel() => Shell.Current.GoToAsync("..");

    /// <summary>
    /// The first date on or after today that falls on <paramref name="dayOfMonth"/>, clamped to
    /// each month's length (31 becomes 30 Sep, but still 31 Oct).
    /// </summary>
    private static DateTime NextDueDate(int dayOfMonth)
    {
        var today = DateTime.Today;
        var thisMonth = OnDay(today.Year, today.Month, dayOfMonth);
        if (thisMonth >= today)
            return thisMonth;

        var next = today.AddMonths(1);
        return OnDay(next.Year, next.Month, dayOfMonth);
    }

    private static DateTime OnDay(int year, int month, int day)
        => new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    private void SyncTypeOptions()
    {
        foreach (var option in TypeOptions)
            option.IsSelected = option.Type == Type;
    }

    private void RefreshCategories()
    {
        var visible = _allCategories
            .Where(c => c.DefaultType == Type || !c.IsSystem)
            .ToList();

        if (SelectedCategory is Category selected && visible.All(c => c.Id != selected.Id))
            SelectedCategory = null;
        SelectedCategory ??= visible.FirstOrDefault();

        CategoryOptions.Clear();
        foreach (var category in visible)
            CategoryOptions.Add(new CategoryOption(category) { IsSelected = category.Id == SelectedCategory?.Id });
    }
}
