using Microsoft.Extensions.Logging;
using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Services;
using Arctrix.PersonalMoneyTracker.ViewModels;
using Arctrix.PersonalMoneyTracker.Views;

namespace Arctrix.PersonalMoneyTracker;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Free for personal/small-business use; required by QuestPDF at startup.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

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

        UseBorderlessInputs();

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
        builder.Services.AddSingleton<IReportService, ReportService>();

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
