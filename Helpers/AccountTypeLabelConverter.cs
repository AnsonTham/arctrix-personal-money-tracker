using System.Globalization;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Helpers;

/// <summary>Display names for <see cref="AccountType"/> values (e.g. EWallet becomes "E-wallet").</summary>
public class AccountTypeLabelConverter : IValueConverter
{
    public static string Label(AccountType type) => type switch
    {
        AccountType.Bank => "Bank",
        AccountType.Cash => "Cash",
        AccountType.EWallet => "E-wallet",
        AccountType.Investment => "Investment",
        _ => type.ToString()
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AccountType type ? Label(type) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
