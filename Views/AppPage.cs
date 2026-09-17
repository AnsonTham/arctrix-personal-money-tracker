using Arctrix.PersonalMoneyTracker.Helpers;

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

    private bool _hasAppeared;
    private bool _entrancePending;

    public AppPage()
    {
        Shell.SetNavBarIsVisible(this, false);
        // If the nav bar is shown (collapsed desktop sidebar), keep the title out of it.
        Shell.SetTitleView(this, new ContentView());
    }

    /// <summary>
    /// Cards that fade and slide in, one after another, the first time the page appears. Pages that
    /// provide any call <see cref="PlayEntranceAsync"/> once their data has loaded.
    /// </summary>
    protected virtual IReadOnlyList<VisualElement> EntranceElements => [];

    /// <summary>
    /// Page transition: the first appearance plays the card entrance when the page has one;
    /// every other appearance (switching back to a section, or a newly pushed form) is a short fade.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        var isFirst = !_hasAppeared;
        _hasAppeared = true;

        if (isFirst && EntranceElements.Count > 0)
        {
            Motion.Hide(EntranceElements);
            _entrancePending = true;
        }
        else if (Content is VisualElement content)
        {
            _ = Motion.FadeInAsync(content);
        }
    }

    /// <summary>Plays the first-appearance card entrance if it hasn't run yet.</summary>
    protected Task PlayEntranceAsync()
    {
        if (!_entrancePending)
            return Task.CompletedTask;

        _entrancePending = false;
        return Motion.RevealAsync(EntranceElements);
    }

    /// <summary>True when the page was pushed onto a section, so its header shows a back button.</summary>
    public bool ShowBackButton
    {
        get => (bool)GetValue(ShowBackButtonProperty);
        set => SetValue(ShowBackButtonProperty, value);
    }
}
