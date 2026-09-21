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
        RuleOptions =
        [
            new RecurrenceRuleOption(RecurrenceRuleType.FixedDayOfMonth, "Day of month"),
            new RecurrenceRuleOption(RecurrenceRuleType.NthWeekdayOfMonth, "Weekday")
        ];
        SyncTypeOptions();
        SyncRuleOptions();
    }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial TransactionType Type { get; set; } = TransactionType.Expense;
    [ObservableProperty] public partial string AmountText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstPaymentLabel))]
    public partial int DayOfMonth { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstPaymentLabel))]
    [NotifyPropertyChangedFor(nameof(UsesFixedDay))]
    [NotifyPropertyChangedFor(nameof(UsesWeekday))]
    public partial RecurrenceRuleType RuleType { get; set; } = RecurrenceRuleType.FixedDayOfMonth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstPaymentLabel))]
    public partial DayOfWeek Weekday { get; set; } = DayOfWeek.Friday;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstPaymentLabel))]
    public partial MonthlyOccurrence Occurrence { get; set; } = MonthlyOccurrence.Last;

    [ObservableProperty] public partial Account? SelectedAccount { get; set; }
    [ObservableProperty] public partial Category? SelectedCategory { get; set; }
    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public IReadOnlyList<FilterOption> TypeOptions { get; }
    public IReadOnlyList<RecurrenceRuleOption> RuleOptions { get; }
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = new();
    public ObservableCollection<Account> Accounts { get; } = new();
    public int[] DaysOfMonth { get; } = Enumerable.Range(1, 31).ToArray();

    /// <summary>Monday first: a pay date is easier to find in a working week.</summary>
    public DayOfWeek[] Weekdays { get; } =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    public MonthlyOccurrence[] Occurrences { get; } = Enum.GetValues<MonthlyOccurrence>();

    public bool UsesFixedDay => RuleType == RecurrenceRuleType.FixedDayOfMonth;
    public bool UsesWeekday => RuleType == RecurrenceRuleType.NthWeekdayOfMonth;

    public bool HasError => ErrorMessage.Length > 0;

    public string FirstPaymentLabel
    {
        get
        {
            var draft = Draft();
            var first = RecurrenceSchedule.FirstOnOrAfter(draft, DateTime.Today);
            var after = RecurrenceSchedule.Next(draft, first);
            return $"First posts on {first:ddd d MMM yyyy}, then {after:ddd d MMM yyyy}, and so on.";
        }
    }

    partial void OnTypeChanged(TransactionType value)
    {
        SyncTypeOptions();
        RefreshCategories();
    }

    partial void OnRuleTypeChanged(RecurrenceRuleType value) => SyncRuleOptions();

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
    private void SelectRule(RecurrenceRuleOption option) => RuleType = option.Rule;

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

        var payment = Draft();
        payment.AccountId = SelectedAccount.Id;
        payment.CategoryId = SelectedCategory.Id;
        payment.Amount = amount;
        payment.Currency = BaseCurrency;
        payment.NextDueDate = RecurrenceSchedule.FirstOnOrAfter(payment, DateTime.Today);
        await _recurring.SaveAsync(payment);

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task Cancel() => Shell.Current.GoToAsync("..");

    /// <summary>The payment as the form currently describes it, for previewing and for saving.</summary>
    private RecurringPayment Draft() => new()
    {
        Name = Name.Trim(),
        Type = Type,
        RuleType = RuleType,
        DayOfMonth = DayOfMonth,
        Weekday = Weekday,
        Occurrence = Occurrence,
        IsActive = true
    };

    private void SyncRuleOptions()
    {
        foreach (var option in RuleOptions)
            option.IsSelected = option.Rule == RuleType;
    }

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
