using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>
/// The list of days that don't count as working days. Seeded with Malaysia's national holidays and
/// entirely the user's to edit after that - state holidays and one-off announcements vary, and
/// nothing here is fetched from anywhere.
/// </summary>
public partial class PublicHolidaysViewModel : ViewModelBase
{
    private readonly IPublicHolidayService _holidays;
    private PublicHoliday? _editing;

    public PublicHolidaysViewModel(IPublicHolidayService holidays)
    {
        _holidays = holidays;
        Title = "Public holidays";
    }

    [ObservableProperty] public partial DateTime NewDate { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddLabel))]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    public partial bool IsEditing { get; set; }

    [ObservableProperty] public partial string CountLabel { get; set; } = string.Empty;

    public ObservableCollection<PublicHolidayRowViewModel> Holidays { get; } = new();

    public bool HasError => ErrorMessage.Length > 0;
    public string AddLabel => IsEditing ? "Save changes" : "Add holiday";
    public string FormTitle => IsEditing ? "Edit holiday" : "Add a holiday";

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var all = await _holidays.GetAllAsync();
            Holidays.Clear();
            foreach (var holiday in all)
                Holidays.Add(new PublicHolidayRowViewModel { Holiday = holiday });

            var upcoming = all.Count(h => h.Date.Date >= DateTime.Today);
            CountLabel = all.Count == 0
                ? "No holidays listed"
                : $"{all.Count} listed · {upcoming} still to come";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(NewName))
        {
            ErrorMessage = "Give the holiday a name.";
            return;
        }

        var clash = Holidays.FirstOrDefault(h => h.Holiday.Date.Date == NewDate.Date && h.Holiday.Id != (_editing?.Id ?? 0));
        if (clash is not null)
        {
            ErrorMessage = $"{NewDate:d MMM yyyy} is already listed as {clash.Holiday.Name}.";
            return;
        }

        await _holidays.SaveAsync(new PublicHoliday
        {
            Id = _editing?.Id ?? 0,
            Date = NewDate.Date,
            Name = NewName.Trim(),
            CreatedAt = _editing?.CreatedAt ?? DateTime.Now
        });

        CancelEdit();
        await LoadAsync();
    }

    [RelayCommand]
    private void Edit(PublicHolidayRowViewModel row)
    {
        _editing = row.Holiday;
        IsEditing = true;
        NewDate = row.Holiday.Date;
        NewName = row.Holiday.Name;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task Remove(PublicHolidayRowViewModel row)
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            $"Remove {row.Holiday.Name}?",
            $"{row.Holiday.Date:dddd d MMMM yyyy} will count as a working day again.",
            "Remove",
            "Keep");
        if (!confirmed)
            return;

        await _holidays.DeleteAsync(row.Holiday.Id);
        if (_editing?.Id == row.Holiday.Id)
            CancelEdit();
        await LoadAsync();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _editing = null;
        IsEditing = false;
        NewName = string.Empty;
        NewDate = DateTime.Today;
        ErrorMessage = string.Empty;
    }
}

/// <summary>One holiday, as the list shows it.</summary>
public class PublicHolidayRowViewModel
{
    public required PublicHoliday Holiday { get; init; }

    public string Name => Holiday.Name;
    public string DateLabel => Holiday.Date.ToString("ddd d MMM yyyy");

    /// <summary>Past holidays are dimmed: they no longer affect any upcoming payment.</summary>
    public bool IsPast => Holiday.Date.Date < DateTime.Today;
}
