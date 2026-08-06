namespace ClxViewer.Services;

/// <summary>
/// Math.Clamp polyfill: not available on .NET Framework 4.8.
/// </summary>
internal static class MathEx
{
    public static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;

    public static long Clamp(long value, long min, long max)
        => value < min ? min : value > max ? max : value;
}
