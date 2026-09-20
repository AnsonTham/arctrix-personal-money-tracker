using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>
/// Add, edit or close a prepaid credit. Saving here records no money: the lump sum was already
/// entered as ordinary income when it arrived (see <see cref="PrepaidCredit"/>).
/// </summary>
public partial class AddPrepaidCreditViewModel : ViewModelBase, IQueryAttributable
{
    private readonly IPrepaidCreditService _credits;
    private readonly IRecurringPaymentService _recurring;
    private readonly ISettingsService _settings;

    private PrepaidCredit? _editing;
    private int? _editId;
    private bool _loaded;

    public AddPrepaidCreditViewModel(
        IPrepaidCreditService credits,
        IRecurringPaymentService recurring,
        ISettingsService settings)
    {
        _credits = credits;
        _recurring = recurring;
        _settings = settings;
        Title = "Add prepaid credit";
    }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string AmountText { get; set; } = string.Empty;
    [ObservableProperty] public partial string MonthsText { get; set; } = "12";
    [ObservableProperty] public partial DateTime StartDate { get; set; } = DateTime.Today;
    [ObservableProperty] public partial RecurringPayment? LinkedPayment { get; set; }
    [ObservableProperty] public partial string BaseCurrency { get; set; } = "MYR";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    public partial bool IsEditing { get; set; }

    /// <summary>The linked-subscription picker, with a "not linked" entry first.</summary>
    public ObservableCollection<RecurringPayment> LinkOptions { get; } = new();

    public bool HasError => ErrorMessage.Length > 0;
    public string SaveLabel => IsEditing ? "Save changes" : "Save credit";

    /// <summary>Live preview of what the card will say, so the numbers can be checked before saving.</summary>
    public string PreviewLabel
    {
        get
        {
            if (!TryRead(out var amount, out var months))
                return "Enter an amount and a number of months.";

            var status = new PrepaidCredit
            {
                TotalAmount = amount,
                MonthsCovered = months,
                StartDate = StartDate,
                Currency = BaseCurrency
            }.StatusOn(DateTime.Today);

            return $"{BaseCurrency} {status.PerMonth:N2} a month · {status.UsageLabel} · {status.RemainingLabel}"
                + $" · runs out {status.RunsOutOn:d MMM yyyy}";
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(Routes.PrepaidCreditIdParam, out var id)
            && int.TryParse(id?.ToString(), out var parsed)
            && parsed > 0)
        {
            _editId = parsed;
        }
    }

    partial void OnAmountTextChanged(string value) => OnPropertyChanged(nameof(PreviewLabel));
    partial void OnMonthsTextChanged(string value) => OnPropertyChanged(nameof(PreviewLabel));
    partial void OnStartDateChanged(DateTime value) => OnPropertyChanged(nameof(PreviewLabel));

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_loaded)
            return;
        _loaded = true;

        BaseCurrency = (await _settings.GetAsync()).BaseCurrency;

        LinkOptions.Clear();
        foreach (var payment in await _recurring.GetAllAsync())
            LinkOptions.Add(payment);

        if (_editId is int id && await _credits.GetByIdAsync(id) is { } existing)
        {
            _editing = existing;
            IsEditing = true;
            Title = "Edit prepaid credit";
            Name = existing.Name;
            AmountText = existing.TotalAmount.ToString("0.##", CultureInfo.CurrentCulture);
            MonthsText = existing.MonthsCovered.ToString(CultureInfo.CurrentCulture);
            StartDate = existing.StartDate;
            LinkedPayment = LinkOptions.FirstOrDefault(p => p.Id == existing.RecurringPaymentId);
        }

        OnPropertyChanged(nameof(PreviewLabel));
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Give this credit a name.";
            return;
        }
        if (!TryRead(out var amount, out var months))
        {
            ErrorMessage = "Enter an amount greater than zero and at least one month.";
            return;
        }

        await _credits.SaveAsync(new PrepaidCredit
        {
            Id = _editing?.Id ?? 0,
            Name = Name.Trim(),
            TotalAmount = amount,
            Currency = BaseCurrency,
            MonthsCovered = months,
            StartDate = StartDate.Date,
            RecurringPaymentId = LinkedPayment?.Id,
            IsClosed = _editing?.IsClosed ?? false,
            // A changed arrangement deserves a fresh reminder when it next runs out.
            CollectionNotifiedAt = null,
            CreatedAt = _editing?.CreatedAt ?? DateTime.Now
        });

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task Close()
    {
        if (_editing is null)
            return;

        var confirmed = await Shell.Current.DisplayAlertAsync(
            $"Close {_editing.Name}?",
            "It stops being counted down and disappears from the Recurring page. No transactions are affected.",
            "Close it",
            "Keep");
        if (!confirmed)
            return;

        await _credits.CloseAsync(_editing.Id);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task Cancel() => Shell.Current.GoToAsync("..");

    private bool TryRead(out decimal amount, out int months) =>
        decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
        & int.TryParse(MonthsText, NumberStyles.Integer, CultureInfo.CurrentCulture, out months)
        && amount > 0
        && months > 0;
}
