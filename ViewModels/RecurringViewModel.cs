using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class RecurringViewModel : ViewModelBase
{
    private readonly IRecurringPaymentService _recurring;
    private readonly IAccountService _accounts;
    private readonly ICategoryService _categories;
    private readonly ISettingsService _settings;
    private readonly ICurrencyService _currency;

    public RecurringViewModel(
        IRecurringPaymentService recurring,
        IAccountService accounts,
        ICategoryService categories,
        ISettingsService settings,
        ICurrencyService currency)
    {
        _recurring = recurring;
        _accounts = accounts;
        _categories = categories;
        _settings = settings;
        _currency = currency;
        Title = "Recurring";
    }

    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";
    [ObservableProperty] public partial decimal MonthlyOutflow { get; set; }
    [ObservableProperty] public partial decimal MonthlyInflow { get; set; }
    [ObservableProperty] public partial string CountLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial string NextUpLabel { get; set; } = string.Empty;

    public ObservableCollection<RecurringRowViewModel> Payments { get; } = new();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            BaseCurrency = (await _settings.GetAsync()).BaseCurrency;

            var payments = (await _recurring.GetAllAsync()).OrderBy(p => p.NextDueDate).ToList();
            var categories = (await _categories.GetAllAsync(includeArchived: true)).ToDictionary(c => c.Id);
            var accounts = (await _accounts.GetAllAsync(includeArchived: true)).ToDictionary(a => a.Id);

            // The oldest unpaid occurrence is the one being retried, so that's the one to show.
            var skips = (await _recurring.GetUnresolvedSkipsAsync())
                .GroupBy(s => s.RecurringPaymentId)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.DueDate).First());

            Payments.Clear();
            foreach (var payment in payments)
            {
                categories.TryGetValue(payment.CategoryId, out var category);
                accounts.TryGetValue(payment.AccountId, out var account);
                skips.TryGetValue(payment.Id, out var skip);
                Payments.Add(new RecurringRowViewModel
                {
                    Payment = payment,
                    CategoryName = category?.Name ?? "Others",
                    CategoryIcon = category?.Icon ?? CategoryIcons.Other,
                    AccountName = account?.Name ?? string.Empty,
                    Skip = skip
                });
            }

            // Upcoming commitments are current state: convert each payment's currency live.
            decimal InBase(RecurringPayment p) => _currency.Convert(p.Amount, p.Currency, BaseCurrency);
            MonthlyOutflow = payments.Where(p => p.Type == TransactionType.Expense).Sum(p => InBase(p));
            MonthlyInflow = payments.Where(p => p.Type == TransactionType.Income).Sum(p => InBase(p));
            CountLabel = payments.Count == 1 ? "1 active payment" : $"{payments.Count} active payments";
            NextUpLabel = Payments.FirstOrDefault() is RecurringRowViewModel next
                ? $"{next.Name} · {next.DueLabel}"
                : "Nothing scheduled";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task AddRecurring() => Shell.Current.GoToAsync(Routes.AddRecurring);

    [RelayCommand]
    private async Task Stop(RecurringRowViewModel row)
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            $"Stop {row.Name}?",
            "No further transactions will be posted for it. Transactions already posted are kept.",
            "Stop",
            "Keep");
        if (!confirmed)
            return;

        await _recurring.DeactivateAsync(row.Payment.Id);
        await LoadAsync();
    }
}
