using CommunityToolkit.Mvvm.ComponentModel;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>A selectable account-type chip.</summary>
public partial class AccountTypeOption : ObservableObject
{
    public AccountTypeOption(AccountType type)
    {
        Type = type;
        Label = AccountTypeLabelConverter.Label(type);
    }

    public AccountType Type { get; }

    public string Label { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}
