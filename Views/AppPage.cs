namespace Arctrix.PersonalMoneyTracker.Views;

/// <summary>
/// Base for every page. Pages draw their own headers, so the Shell navigation bar starts hidden
/// and never shows a duplicate title; AppShell only shows it where it carries the menu button
/// (the collapsed desktop sidebar) and tells the page whether to show its in-page back button.
/// </summary>
public class AppPage : ContentPage
{
    public static readonly BindableProperty ShowBackButtonProperty = BindableProperty.Create(
        nameof(ShowBackButton), typeof(bool), typeof(AppPage), false);

    public AppPage()
    {
        Shell.SetNavBarIsVisible(this, false);
        // If the nav bar is shown (collapsed desktop sidebar), keep the title out of it.
        Shell.SetTitleView(this, new ContentView());
    }

    /// <summary>True when the page was pushed onto a section, so its header shows a back button.</summary>
    public bool ShowBackButton
    {
        get => (bool)GetValue(ShowBackButtonProperty);
        set => SetValue(ShowBackButtonProperty, value);
    }
}
