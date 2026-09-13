using System.Globalization;

namespace Arctrix.PersonalMoneyTracker.Controls;

/// <summary>Axis helpers shared by the chart controls: clean tick steps and compact tick labels.</summary>
internal static class ChartScale
{
    /// <summary>Expands [min, max] outward to clean tick boundaries with roughly three intervals.</summary>
    public static (double Min, double Max, double Step) Nice(double min, double max)
    {
        if (max - min < 1e-9)
        {
            var pad = Math.Max(Math.Abs(max) * 0.1, 1);
            min -= pad;
            max += pad;
        }

        var step = NiceStep((max - min) / 3);
        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
    }

    /// <summary>Short tick label: 950, 6.5K, 1.2M.</summary>
    public static string Compact(double value)
    {
        var abs = Math.Abs(value);
        return abs >= 1_000_000 ? (value / 1_000_000).ToString("0.#", CultureInfo.CurrentCulture) + "M"
            : abs >= 1_000 ? (value / 1_000).ToString("0.#", CultureInfo.CurrentCulture) + "K"
            : value.ToString("0.#", CultureInfo.CurrentCulture);
    }

    private static double NiceStep(double rough)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var fraction = rough / magnitude;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return nice * magnitude;
    }
}
