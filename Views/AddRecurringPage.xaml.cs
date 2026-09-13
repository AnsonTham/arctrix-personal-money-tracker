using Arctrix.PersonalMoneyTracker.ViewModels;

namespace Arctrix.PersonalMoneyTracker.Views;

public partial class AddRecurringPage : ContentPage
{
    private readonly AddRecurringViewModel _viewModel;

    public AddRecurringPage(AddRecurringViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
