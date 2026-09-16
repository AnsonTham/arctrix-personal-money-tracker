using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace Arctrix.PersonalMoneyTracker;

/// <summary>
/// Tapping the bottom tab that is already selected returns it to its main page, closing any page
/// pushed on top (for example Accounts opened from More). Shell leaves pushed pages in place.
/// </summary>
public class ArctrixShellRenderer : ShellRenderer
{
    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem shellItem) =>
        new ReselectingShellItemRenderer(this);

    private sealed class ReselectingShellItemRenderer(IShellContext shellContext) : ShellItemRenderer(shellContext)
    {
        protected override async void OnTabReselected(ShellSection shellSection)
        {
            base.OnTabReselected(shellSection);

            if (shellSection.Navigation.NavigationStack.Count > 1)
                await shellSection.Navigation.PopToRootAsync();
        }
    }
}
