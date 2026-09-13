using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.ViewModels;

namespace Arctrix.PersonalMoneyTracker.Views;

public partial class AddEditTransactionPage : ContentPage
{
    private const double FormMaxWidth = 760;

    private readonly AddEditTransactionViewModel _viewModel;

    public AddEditTransactionPage(AddEditTransactionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        Responsive.Apply(LayoutRoot, width, FormMaxWidth);
    }
}
