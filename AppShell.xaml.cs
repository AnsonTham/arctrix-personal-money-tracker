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

    public AppShell()
    {
        InitializeComponent();

        // Forms reachable from anywhere; pushed onto the current section's stack.
        Routing.RegisterRoute(Routes.AddTransaction, typeof(AddEditTransactionPage));
        Routing.RegisterRoute(Routes.AddAccount, typeof(AddAccountPage));
        Routing.RegisterRoute(Routes.AddRecurring, typeof(AddRecurringPage));

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

        SizeChanged += (_, _) => UpdateSidebarMode();
    }

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
        if (Width <= 0)
            return;

        var mode = Width < SidebarCollapseWidth ? FlyoutBehavior.Flyout : FlyoutBehavior.Locked;
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
