namespace Arctrix.PersonalMoneyTracker.Helpers;

/// <summary>
/// Navigates to a top-level section regardless of layout: sections that are root Shell
/// items (sidebar entries, phone tabs) are switched to; the rest (phone "More" sections)
/// are pushed.
/// </summary>
public static class AppNavigation
{
    public static Task GoToSectionAsync(string route)
    {
        var shell = Shell.Current;
        var isRootItem = shell.Items
            .SelectMany(item => item.Items)
            .SelectMany(section => section.Items)
            .Any(content => content.Route == route);

        return shell.GoToAsync(isRootItem ? "//" + route : route);
    }
}
