using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.ViewModels;

namespace Arctrix.PersonalMoneyTracker.Views;

public partial class AddEditTransactionPage : AppPage
{
    private const double FormMaxWidth = 760;

    private readonly AddEditTransactionViewModel _viewModel;

    public AddEditTransactionPage(AddEditTransactionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
#if ANDROID || IOS
        ScanReceiptHost.Content = CreateScanReceiptButton();
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);

        // Backing out of a scanned transaction without saving shouldn't leave its photo behind.
        if (args.NavigationType is NavigationType.Pop or NavigationType.PopToRoot)
            _viewModel.DiscardUnsavedReceipt();
    }

#if ANDROID || IOS
    // Receipt scanning is mobile-only, so the button only exists in Android and iOS builds.
    private Button CreateScanReceiptButton()
    {
        var button = new Button { ImageSource = "icon_camera.png", Command = _viewModel.ScanReceiptCommand };
        if (Application.Current?.Resources.TryGetValue("IconButton", out var style) == true)
            button.Style = style as Style;
        SemanticProperties.SetDescription(button, "Scan a receipt");
        button.SetBinding(IsVisibleProperty, static (AddEditTransactionViewModel vm) => vm.CanScanReceipt);
        return button;
    }
#endif

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        Responsive.Apply(LayoutRoot, width, FormMaxWidth);
    }
}
