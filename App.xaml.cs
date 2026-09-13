namespace Arctrix.PersonalMoneyTracker;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // The design system is dark-only; keep native pickers and dialogs in step with it.
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new AppShell()) { Title = "Arctrix Personal Money Tracker" };
}
