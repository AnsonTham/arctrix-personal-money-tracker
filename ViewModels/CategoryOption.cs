using CommunityToolkit.Mvvm.ComponentModel;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>A selectable category chip.</summary>
public partial class CategoryOption : ObservableObject
{
    public CategoryOption(Category category)
    {
        Category = category;
    }

    public Category Category { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}
