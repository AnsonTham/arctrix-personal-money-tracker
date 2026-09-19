#if WINDOWS
using Arctrix.PersonalMoneyTracker.Services.Telegram;
#endif

namespace Arctrix.PersonalMoneyTracker;

public partial class App : Application
{
#if WINDOWS
    private readonly TelegramBotService _bot;
#endif

    public App(IServiceProvider services)
    {
        InitializeComponent();

        // The design system is dark-only; keep native pickers and dialogs in step with it.
        UserAppTheme = AppTheme.Dark;

#if WINDOWS
        _bot = services.GetRequiredService<TelegramBotService>();
#endif
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

#if WINDOWS
        // The Telegram bot runs alongside the window: it catches up on messages that arrived while
        // the app was closed, then keeps checking the relay until the window goes away.
        window.Created += (_, _) => _bot.Start();
        window.Destroying += async (_, _) => await _bot.DisposeAsync();
#endif

        return window;
    }
}
