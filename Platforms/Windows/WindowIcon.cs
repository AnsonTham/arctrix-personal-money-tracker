using System.Runtime.InteropServices;

namespace Arctrix.PersonalMoneyTracker;

/// <summary>
/// Gives the window the executable's own icon. The taskbar and the desktop read the icon straight
/// out of the .exe, but a window has its own pair of icons, and MAUI leaves them unset - which
/// leaves the title bar and alt-tab showing nothing. Both are taken from the running executable, so
/// they can never disagree with ApplicationIcon.
/// </summary>
internal static class WindowIcon
{
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    /// <summary>Title bar and alt-tab sizes, as Windows asks for them.</summary>
    private const int SM_CXSMICON = 49;
    private const int SM_CXICON = 11;

    public static void Apply(Microsoft.Maui.Controls.Window window)
    {
        if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native)
            return;

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(native);
        if (handle == IntPtr.Zero)
            return;

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
            return;

        Set(handle, executable, ICON_SMALL, GetSystemMetrics(SM_CXSMICON));
        Set(handle, executable, ICON_BIG, GetSystemMetrics(SM_CXICON));

        ShowInTitleBar(handle, executable);
    }

    /// <summary>
    /// Asks the title bar to show the icon and its system menu. WinUI only honours this when the
    /// app is not drawing its own title bar; if MAUI is, the call is harmless and the icon still
    /// appears in alt-tab and the taskbar.
    /// </summary>
    private static void ShowInTitleBar(IntPtr handle, string executable)
    {
        try
        {
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
            if (appWindow?.TitleBar is not { } titleBar)
                return;

            var icons = new IntPtr[1];
            if (PrivateExtractIcons(executable, 0, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CXSMICON), icons, new int[1], 1, 0) > 0
                && icons[0] != IntPtr.Zero)
            {
                appWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(icons[0]));
            }

            titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.ShowIconAndSystemMenu;
        }
        catch (Exception ex) when (ex is COMException or NotSupportedException)
        {
            // An older Windows build without this title-bar API: the window icons above still stand.
            System.Diagnostics.Debug.WriteLine($"Couldn't show the icon in the title bar: {ex.Message}");
        }
    }

    private static void Set(IntPtr window, string executable, int which, int size)
    {
        var icons = new IntPtr[1];
        // The first icon group in the executable is the application icon.
        if (PrivateExtractIcons(executable, 0, size, size, icons, new int[1], 1, 0) <= 0 || icons[0] == IntPtr.Zero)
            return;

        // The window keeps the handle for as long as it lives, so it is not destroyed here.
        SendMessage(window, WM_SETICON, which, icons[0]);
    }

    // Plain DllImport rather than LibraryImport: the generated marshalling code needs unsafe
    // blocks enabled for the whole project, which three calls don't justify.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, int count, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, int wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
