using Arctrix.PersonalMoneyTracker.ViewModels;

namespace Arctrix.PersonalMoneyTracker.Views;

public partial class ReceiptPhotoPage : AppPage
{
    public ReceiptPhotoPage(ReceiptPhotoViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
