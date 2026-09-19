using Microsoft.Extensions.Logging;
using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Services;
using Arctrix.PersonalMoneyTracker.ViewModels;
using Arctrix.PersonalMoneyTracker.Views;

namespace Arctrix.PersonalMoneyTracker;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
#if WINDOWS
        // Free for personal/small-business use; required by QuestPDF at startup.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
#endif

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Inter-Regular.ttf", "InterRegular");
                fonts.AddFont("Inter-Medium.ttf", "InterMedium");
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
                fonts.AddFont("Inter-Bold.ttf", "InterBold");
            });

#if ANDROID
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Shell, ArctrixShellRenderer>());
#endif

        UseBorderlessInputs();
        UsePressFeedback();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Data
        builder.Services.AddSingleton<AppDbContext>();

        // Services (singleton: one SQLite connection/state per app lifetime)
        builder.Services.AddSingleton<IAccountService, AccountService>();
        builder.Services.AddSingleton<ICategoryService, CategoryService>();
        builder.Services.AddSingleton<ITransactionService, TransactionService>();
        builder.Services.AddSingleton<IRecurringPaymentService, RecurringPaymentService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<ICurrencyService, CurrencyService>();
        builder.Services.AddSingleton<IReceiptPhotoStore, ReceiptPhotoStore>();
#if ANDROID
        builder.Services.AddSingleton<IReceiptOcrService, MlKitReceiptOcrService>();
#elif IOS
        builder.Services.AddSingleton<IReceiptOcrService, VisionReceiptOcrService>();
#elif WINDOWS
        // No scanning UI on desktop; this reads receipts sent to the Telegram bot.
        builder.Services.AddSingleton<IReceiptOcrService, WindowsReceiptOcrService>();
        builder.Services.AddSingleton<Services.Telegram.TelegramBotService>();
#endif
#if WINDOWS
        builder.Services.AddSingleton<IReportService, ReportService>();
#else
        // QuestPDF has no Android/iOS renderer; the Reports page shows a notice instead.
        builder.Services.AddSingleton<IReportService, UnsupportedReportService>();
#endif

        // ViewModels (transient: fresh state each time a page is navigated to)
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<AccountsViewModel>();
        builder.Services.AddTransient<AddAccountViewModel>();
        builder.Services.AddTransient<HistoryViewModel>();
        builder.Services.AddTransient<AddEditTransactionViewModel>();
        builder.Services.AddTransient<AnalyticsViewModel>();
        builder.Services.AddTransient<RecurringViewModel>();
        builder.Services.AddTransient<AddRecurringViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<ReportsViewModel>();
        builder.Services.AddTransient<MoreViewModel>();

        // Pages
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<AccountsPage>();
        builder.Services.AddTransient<AddAccountPage>();
        builder.Services.AddTransient<HistoryPage>();
        builder.Services.AddTransient<AddEditTransactionPage>();
        builder.Services.AddTransient<AnalyticsPage>();
        builder.Services.AddTransient<RecurringPage>();
        builder.Services.AddTransient<AddRecurringPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ReportsPage>();
        builder.Services.AddTransient<MorePage>();

#if ANDROID || IOS
        // Receipt scanning (mobile only)
        builder.Services.AddTransient<ScanReceiptViewModel>();
        builder.Services.AddTransient<ReceiptPhotoViewModel>();
        builder.Services.AddTransient<ScanReceiptPage>();
        builder.Services.AddTransient<ReceiptPhotoPage>();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Inputs are drawn inside design-system InputShell borders, so strip each platform's
    /// own underline/border. WinUI chrome is removed via theme resources in Platforms/Windows/App.xaml.
    /// </summary>
    private static void UseBorderlessInputs()
    {
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(nameof(UseBorderlessInputs), (handler, _) => RemoveNativeChrome(handler.PlatformView));
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping(nameof(UseBorderlessInputs), (handler, _) => RemoveNativeChrome(handler.PlatformView));
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping(nameof(UseBorderlessInputs), (handler, _) => RemoveNativeChrome(handler.PlatformView));
        Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping(nameof(UseBorderlessInputs), (handler, _) => RemoveNativeChrome(handler.PlatformView));
    }

    /// <summary>Every button shrinks slightly while pressed (see <see cref="Motion.AddPressFeedback"/>).</summary>
    private static void UsePressFeedback() =>
        Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping(nameof(UsePressFeedback), (_, view) =>
        {
            if (view is Button button)
                Motion.AddPressFeedback(button);
        });

    private static void RemoveNativeChrome(object platformView)
    {
#if ANDROID
        if (platformView is Android.Views.View view)
            view.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif IOS
        if (platformView is UIKit.UITextField field)
            field.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
    }
}
