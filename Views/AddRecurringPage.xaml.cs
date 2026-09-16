using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.ViewModels;

namespace Arctrix.PersonalMoneyTracker.Views;

public partial class AddRecurringPage : AppPage
{
    private const double FormMaxWidth = 640;

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

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        Responsive.Apply(LayoutRoot, width, FormMaxWidth);
    }
}
