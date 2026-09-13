using System.Globalization;

namespace Arctrix.PersonalMoneyTracker.Helpers;

/// <summary>Converts a stored hex string (e.g. Account.ColorHex) into a Color.</summary>
public class HexColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string hex && Color.TryParse(hex, out var color) ? color : Color.FromArgb("#8F98A7");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
