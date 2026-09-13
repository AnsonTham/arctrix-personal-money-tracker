using CommunityToolkit.Mvvm.ComponentModel;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;
}
