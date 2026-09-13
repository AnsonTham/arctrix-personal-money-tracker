using CommunityToolkit.Mvvm.ComponentModel;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>
/// A selectable transaction-type chip, used for list filters and the type picker.
/// A null <see cref="Type"/> means "all".
/// </summary>
public partial class FilterOption : ObservableObject
{
    public FilterOption(string name, TransactionType? type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }

    public TransactionType? Type { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}
