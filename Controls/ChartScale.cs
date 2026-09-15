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

    /// <summary>
    /// Short tick label: 950, 6.5K, 1.25M. Shows enough decimals that ticks <paramref name="step"/>
    /// apart stay distinct (1,250 and 1,300 are "1.25K" and "1.3K", not both "1.3K").
    /// </summary>
    public static string Compact(double value, double step = 0)
    {
        var abs = Math.Abs(value);
        var (divisor, suffix) = abs >= 1_000_000 ? (1_000_000d, "M") : abs >= 1_000 ? (1_000d, "K") : (1d, "");
        var decimals = step > 0 ? Math.Clamp((int)Math.Ceiling(-Math.Log10(step / divisor) - 1e-9), 0, 3) : 1;
        var format = decimals == 0 ? "0" : "0." + new string('#', decimals);
        return (value / divisor).ToString(format, CultureInfo.CurrentCulture) + suffix;
    }

    private static double NiceStep(double rough)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var fraction = rough / magnitude;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return nice * magnitude;
    }
}
