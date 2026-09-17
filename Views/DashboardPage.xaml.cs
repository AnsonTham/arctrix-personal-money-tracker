using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.ViewModels;

namespace Arctrix.PersonalMoneyTracker.Views;

public partial class DashboardPage : AppPage
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
#if ANDROID || IOS
        ScanReceiptHost.Content = CreateScanReceiptButton();
        ScanReceiptHost.IsVisible = true;
#endif
    }

#if ANDROID || IOS
    // Receipt scanning is mobile-only, so this quick action only exists in Android and iOS builds.
    private Button CreateScanReceiptButton()
    {
        var button = new Button
        {
            Text = "Scan receipt",
            ImageSource = "icon_camera.png",
            ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 8),
            Command = _viewModel.ScanReceiptCommand
        };
        if (Application.Current?.Resources.TryGetValue("SecondaryButton", out var style) == true)
            button.Style = style as Style;
        return button;
    }
#endif

    protected override IReadOnlyList<VisualElement> EntranceElements =>
        [Header, QuickActions, HeroCard, StatsGrid, RecentCard, SpendingCard, AccountsCard];

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        finally
        {
            await PlayEntranceAsync();
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        Responsive.Apply(LayoutRoot, width);
    }
}
