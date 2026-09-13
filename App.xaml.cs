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
    {
        var window = new Window(new AppShell()) { Title = "Arctrix Personal Money Tracker" };

        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
        {
            window.Width = 1360;
            window.Height = 880;
            window.MinimumWidth = 380;
            window.MinimumHeight = 640;
        }

        return window;
    }
}
