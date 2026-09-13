using Arctrix.PersonalMoneyTracker.ViewModels;

namespace Arctrix.PersonalMoneyTracker.Views;

public partial class AddAccountPage : ContentPage
{
    public AddAccountPage(AddAccountViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
