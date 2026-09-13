using System.Collections.ObjectModel;
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
        Title = "Add Recurring Payment";
    }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial TransactionType Type { get; set; } = TransactionType.Expense;
    [ObservableProperty] public partial decimal Amount { get; set; }
    [ObservableProperty] public partial int DayOfMonth { get; set; } = 1;
    [ObservableProperty] public partial Account? SelectedAccount { get; set; }
    [ObservableProperty] public partial Category? SelectedCategory { get; set; }
    [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";

    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public TransactionType[] TransactionTypes { get; } = { TransactionType.Expense, TransactionType.Income };
    public int[] DaysOfMonth { get; } = Enumerable.Range(1, 31).ToArray();

    [RelayCommand]
    public async Task LoadAsync()
    {
        var settings = await _settings.GetAsync();
        BaseCurrency = settings.BaseCurrency;

        var accounts = await _accounts.GetAllAsync();
        Accounts.Clear();
        foreach (var a in accounts) Accounts.Add(a);
        SelectedAccount ??= Accounts.FirstOrDefault();

        var categories = await _categories.GetAllAsync();
        Categories.Clear();
        foreach (var c in categories) Categories.Add(c);
        SelectedCategory ??= Categories.FirstOrDefault();
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Give this payment a name.";
            return;
        }
        if (Amount <= 0)
        {
            ErrorMessage = "Enter an amount greater than zero.";
            return;
        }
        if (SelectedAccount is null || SelectedCategory is null)
        {
            ErrorMessage = "Choose an account and category.";
            return;
        }

        var today = DateTime.Now;
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var day = Math.Min(DayOfMonth, daysInMonth);
        var nextDue = new DateTime(today.Year, today.Month, day);
        if (nextDue < today.Date) nextDue = nextDue.AddMonths(1);

        await _recurring.SaveAsync(new RecurringPayment
        {
            Name = Name.Trim(),
            Type = Type,
            AccountId = SelectedAccount.Id,
            CategoryId = SelectedCategory.Id,
            Amount = Amount,
            Currency = BaseCurrency,
            DayOfMonth = DayOfMonth,
            NextDueDate = nextDue,
            IsActive = true
        });

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task Cancel() => Shell.Current.GoToAsync("..");
}
