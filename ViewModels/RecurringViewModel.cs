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
    private readonly IPrepaidCreditService _credits;
    private readonly IAccountService _accounts;
    private readonly ICategoryService _categories;
    private readonly ISettingsService _settings;
    private readonly ICurrencyService _currency;

    public RecurringViewModel(
        IRecurringPaymentService recurring,
        IPrepaidCreditService credits,
        IAccountService accounts,
        ICategoryService categories,
        ISettingsService settings,
        ICurrencyService currency)
    {
        _recurring = recurring;
        _credits = credits;
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

    /// <summary>Prepaid pools counted down alongside the payments they cover. Informational only.</summary>
    public ObservableCollection<PrepaidCreditRowViewModel> PrepaidCredits { get; } = new();

    public bool HasPrepaidCredits => PrepaidCredits.Count > 0;

    /// <summary>The card explains itself when empty, rather than showing a bare heading.</summary>
    public bool ShowPrepaidEmptyState => PrepaidCredits.Count == 0;

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
            await LoadPrepaidCreditsAsync(payments);

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

    /// <summary>
    /// Works out where each prepaid credit stands today. Nothing is written: the figures are derived
    /// from the lump sum and its start date every time they are shown.
    /// </summary>
    private async Task LoadPrepaidCreditsAsync(List<RecurringPayment> payments)
    {
        var today = DateTime.Today;
        var names = payments.ToDictionary(p => p.Id, p => p.Name);

        PrepaidCredits.Clear();
        foreach (var credit in await _credits.GetAllAsync())
        {
            PrepaidCredits.Add(new PrepaidCreditRowViewModel
            {
                Credit = credit,
                Status = credit.StatusOn(today),
                LinkedPaymentName = credit.RecurringPaymentId is int id && names.TryGetValue(id, out var name) ? name : string.Empty
            });
        }

        OnPropertyChanged(nameof(HasPrepaidCredits));
        OnPropertyChanged(nameof(ShowPrepaidEmptyState));
    }

    [RelayCommand]
    private Task AddRecurring() => Shell.Current.GoToAsync(Routes.AddRecurring);

    [RelayCommand]
    private Task AddPrepaidCredit() => Shell.Current.GoToAsync(Routes.AddPrepaidCredit);

    [RelayCommand]
    private Task EditPrepaidCredit(PrepaidCreditRowViewModel row) =>
        Shell.Current.GoToAsync($"{Routes.AddPrepaidCredit}?{Routes.PrepaidCreditIdParam}={row.Id}");

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
