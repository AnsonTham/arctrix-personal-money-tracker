namespace Arctrix.PersonalMoneyTracker.Helpers;

/// <summary>
/// Width-based layout switching for pages. Driven by the page's own width rather than the
/// window's, so the desktop sidebar is already accounted for.
/// </summary>
public static class Responsive
{
    public const double WideBreakpoint = 760;
    public const double MaxContentWidth = 1240;

    /// <summary>
    /// Puts <paramref name="layoutRoot"/> into its "Wide" or "Narrow" visual state and caps
    /// its width so desktop content doesn't stretch edge to edge.
    /// </summary>
    public static void Apply(VisualElement layoutRoot, double pageWidth)
    {
        if (pageWidth <= 0)
            return;

        VisualStateManager.GoToState(layoutRoot, pageWidth >= WideBreakpoint ? "Wide" : "Narrow");

        var width = Math.Min(pageWidth, MaxContentWidth);
        if (Math.Abs(layoutRoot.WidthRequest - width) > 0.5)
            layoutRoot.WidthRequest = width;
    }
}
