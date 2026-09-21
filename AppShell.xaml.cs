using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Views;

namespace Arctrix.PersonalMoneyTracker;

/// <summary>
/// Navigation chrome. Phones get a bottom tab bar (Home, History, Add, Analytics, More);
/// desktop and tablet get a sidebar listing every section, which collapses into a
/// flyout when the window is narrower than <see cref="SidebarCollapseWidth"/>.
/// </summary>
public partial class AppShell : Shell
{
    private const double SidebarCollapseWidth = 900;

    private readonly bool _usesTabBar = DeviceInfo.Current.Idiom == DeviceIdiom.Phone;
    private Window? _window;

    public AppShell()
    {
        InitializeComponent();

        // Forms reachable from anywhere; pushed onto the current section's stack.
        Routing.RegisterRoute(Routes.AddTransaction, typeof(AddEditTransactionPage));
        Routing.RegisterRoute(Routes.AddAccount, typeof(AddAccountPage));
        Routing.RegisterRoute(Routes.EditAccount, typeof(EditAccountPage));
        Routing.RegisterRoute(Routes.PublicHolidays, typeof(PublicHolidaysPage));
        Routing.RegisterRoute(Routes.AddRecurring, typeof(AddRecurringPage));
        Routing.RegisterRoute(Routes.AddPrepaidCredit, typeof(AddPrepaidCreditPage));
#if ANDROID || IOS
        // Receipt scanning is mobile-only; desktop builds don't include these pages.
        Routing.RegisterRoute(Routes.ScanReceipt, typeof(ScanReceiptPage));
        Routing.RegisterRoute(Routes.ReceiptPhoto, typeof(ReceiptPhotoPage));
#endif

        if (_usesTabBar)
            BuildTabBar();
        else
            BuildSidebar();
    }

    private void BuildSidebar()
    {
        FlyoutBehavior = FlyoutBehavior.Locked;

        Items.Add(CreateSection("Dashboard", "icon_dashboard.png", Routes.Dashboard, typeof(DashboardPage)));
        Items.Add(CreateSection("Transactions", "icon_history.png", Routes.History, typeof(HistoryPage)));
        Items.Add(CreateSection("Accounts", "icon_accounts.png", Routes.Accounts, typeof(AccountsPage)));
        Items.Add(CreateSection("Analytics", "icon_analytics.png", Routes.Analytics, typeof(AnalyticsPage)));
        Items.Add(CreateSection("Recurring", "icon_recurring.png", Routes.Recurring, typeof(RecurringPage)));
        Items.Add(CreateSection("Reports", "icon_reports.png", Routes.Reports, typeof(ReportsPage)));
        Items.Add(CreateSection("Settings", "icon_settings.png", Routes.Settings, typeof(SettingsPage)));
    }

    // The sidebar mode follows the window: Shell's own SizeChanged is not raised on Windows, which
    // left the sidebar locked open in narrow windows and squeezed pages into a sliver.
    protected override void OnParentChanged()
    {
        base.OnParentChanged();
        if (_usesTabBar)
            return;

        if (_window is not null)
            _window.SizeChanged -= OnWindowSizeChanged;

        _window = Parent as Window;
        if (_window is not null)
        {
            _window.SizeChanged += OnWindowSizeChanged;
            UpdateSidebarMode();
        }
    }

    private void OnWindowSizeChanged(object? sender, EventArgs e) => UpdateSidebarMode();

    /// <summary>
    /// Selecting the section that is already open returns it to its main page, closing any form
    /// pushed on top. Shell ignores a selection of the current item, so this is handled here.
    /// </summary>
    private async void OnSidebarItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject { BindingContext: BaseShellItem tapped } || !IsCurrentSection(tapped))
            return;

        if (CurrentPage is Page page && page.Navigation.NavigationStack.Count > 1)
            await page.Navigation.PopToRootAsync();
    }

    private bool IsCurrentSection(BaseShellItem item) =>
        item == CurrentItem || item == CurrentItem?.CurrentItem || item == CurrentItem?.CurrentItem?.CurrentItem;

    private void BuildTabBar()
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;

        // On phones the secondary sections live behind "More", so they are pushed as routes.
        Routing.RegisterRoute(Routes.Accounts, typeof(AccountsPage));
        Routing.RegisterRoute(Routes.Recurring, typeof(RecurringPage));
        Routing.RegisterRoute(Routes.Reports, typeof(ReportsPage));
        Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));

        var tabs = new TabBar();
        tabs.Items.Add(CreateTab("Home", "icon_dashboard.png", Routes.Dashboard, typeof(DashboardPage)));
        tabs.Items.Add(CreateTab("History", "icon_history.png", Routes.History, typeof(HistoryPage)));
        tabs.Items.Add(CreateTab("Add", "icon_add.png", Routes.QuickAdd, typeof(ContentPage)));
        tabs.Items.Add(CreateTab("Analytics", "icon_analytics.png", Routes.Analytics, typeof(AnalyticsPage)));
        tabs.Items.Add(CreateTab("More", "icon_more.png", Routes.More, typeof(MorePage)));
        Items.Add(tabs);
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);

        // "Add" is an action rather than a destination: open the transaction form over the current tab.
        if (_usesTabBar && args.Target.Location.OriginalString.EndsWith("/" + Routes.QuickAdd, StringComparison.Ordinal))
        {
            args.Cancel();
            Dispatcher.Dispatch(async () => await GoToAsync(Routes.AddTransaction));
        }
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        UpdateNavBar();
    }

    private void UpdateSidebarMode()
    {
        var width = _window?.Width ?? -1;
        if (double.IsNaN(width) || width <= 0)
            return;

        var mode = width < SidebarCollapseWidth ? FlyoutBehavior.Flyout : FlyoutBehavior.Locked;
        if (FlyoutBehavior == mode)
            return;

        FlyoutBehavior = mode;
        FlyoutIsPresented = false;
        UpdateNavBar();
    }

    /// <summary>
    /// Pages draw their own headers, so the nav bar stays hidden - except on section pages while
    /// the desktop sidebar is collapsed, where it carries the menu button. Pushed pages (forms,
    /// and More's sections on phones) show the back button in their own header instead.
    /// </summary>
    private void UpdateNavBar()
    {
        if (CurrentPage is not Page page)
            return;

        var isPushed = page.Navigation.NavigationStack.Count > 1;
        SetNavBarIsVisible(page, !isPushed && !_usesTabBar && FlyoutBehavior == FlyoutBehavior.Flyout);

        if (page is AppPage appPage)
            appPage.ShowBackButton = isPushed;

        if (isPushed)
            SetBackButtonBehavior(page, new BackButtonBehavior { IsVisible = false });
    }

    private static FlyoutItem CreateSection(string title, string icon, string route, Type pageType)
    {
        var item = new FlyoutItem { Title = title, Icon = icon };
        item.Items.Add(CreateContent(title, route, pageType));
        return item;
    }

    private static Tab CreateTab(string title, string icon, string route, Type pageType)
    {
        var tab = new Tab { Title = title, Icon = icon };
        tab.Items.Add(CreateContent(title, route, pageType));
        return tab;
    }

    private static ShellContent CreateContent(string title, string route, Type pageType) => new()
    {
        Title = title,
        Route = route,
        ContentTemplate = new DataTemplate(pageType)
    };
}
