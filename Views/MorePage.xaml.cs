using Arctrix.PersonalMoneyTracker.ViewModels;

namespace Arctrix.PersonalMoneyTracker.Views;

public partial class MorePage : ContentPage
{
    public MorePage(MoreViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
