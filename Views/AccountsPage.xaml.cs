using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.ViewModels;

namespace Arctrix.PersonalMoneyTracker.Views;

public partial class AccountsPage : AppPage
{
    private readonly AccountsViewModel _viewModel;

    public AccountsPage(AccountsViewModel viewModel)
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
        Responsive.Apply(LayoutRoot, width);

        var span = width >= Responsive.WideBreakpoint ? 2 : 1;
        if (AccountGridLayout.Span != span)
            AccountGridLayout.Span = span;
    }
}
