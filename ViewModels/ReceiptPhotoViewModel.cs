using CommunityToolkit.Mvvm.ComponentModel;
using Arctrix.PersonalMoneyTracker.Helpers;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>Shows the original receipt photo attached to a transaction.</summary>
public partial class ReceiptPhotoViewModel : ViewModelBase, IQueryAttributable
{
    public ReceiptPhotoViewModel() => Title = "Receipt photo";

    [ObservableProperty] public partial string? PhotoPath { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(Routes.ReceiptPathParam, out var path) && path is string fullPath)
            PhotoPath = fullPath;
    }
}
