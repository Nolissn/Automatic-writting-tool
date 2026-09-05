#nullable enable

using System.Globalization;

namespace OpenRoadTyper.Core.Typing;

/// <summary>
/// Pure helpers around the "typing interval" setting shared by both UIs, so the
/// seconds/milliseconds conversion and display formatting behave identically
/// on Windows and Linux.
/// </summary>
public static class TypingSpeedFormatter
{
    private const decimal MillisecondsPerSecond = 1000m;

    public static int Clamp(int value, int minimum, int maximum)
    {
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }

    public static decimal ConvertToMilliseconds(decimal value, bool useSeconds)
    {
        return useSeconds ? value * MillisecondsPerSecond : value;
    }

    public static decimal GetDisplayValue(decimal milliseconds, bool useSeconds)
    {
        return useSeconds ? milliseconds / MillisecondsPerSecond : milliseconds;
    }

    public static string Format(decimal milliseconds, bool useSeconds)
    {
        var value = GetDisplayValue(milliseconds, useSeconds);
        var unit = useSeconds ? "s" : "ms";
        return $"{value.ToString("0.###", CultureInfo.CurrentCulture)} {unit}";
    }
}
