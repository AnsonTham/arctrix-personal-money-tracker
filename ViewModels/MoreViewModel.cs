using CommunityToolkit.Mvvm.Input;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>Phone-only hub for the sections that don't fit in the tab bar.</summary>
public partial class MoreViewModel : ViewModelBase
{
    public MoreViewModel()
    {
        Title = "More";
    }

    [RelayCommand]
    private Task GoTo(string route) => Shell.Current.GoToAsync(route);
}
