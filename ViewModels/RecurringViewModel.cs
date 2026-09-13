using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arctrix.PersonalMoneyTracker.Models;
using Arctrix.PersonalMoneyTracker.Services;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public partial class RecurringViewModel : ViewModelBase
{
    private readonly IRecurringPaymentService _recurring;
    private readonly IAccountService _accounts;
    private readonly ICategoryService _categories;

    public RecurringViewModel(IRecurringPaymentService recurring, IAccountService accounts, ICategoryService categories)
    {
        _recurring = recurring;
        _accounts = accounts;
        _categories = categories;
        Title = "Recurring";
    }

    public ObservableCollection<RecurringPayment> Payments { get; } = new();

    [ObservableProperty] public partial bool IsEmpty { get; set; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var all = await _recurring.GetAllAsync();
            Payments.Clear();
            foreach (var p in all.OrderBy(p => p.NextDueDate)) Payments.Add(p);
            IsEmpty = Payments.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Deactivate(RecurringPayment payment)
    {
        await _recurring.DeactivateAsync(payment.Id);
        await LoadAsync();
    }
}
