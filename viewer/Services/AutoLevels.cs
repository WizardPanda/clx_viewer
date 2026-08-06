namespace ClxViewer.Services;

/// <summary>A windowing target: raw low/high plus an optional display gain.</summary>
public readonly record struct Levels(double Low, double High, double Gain);

/// <summary>
/// Auto min/max computation. Shares the exact constants with the native
/// thumbnail provider so Explorer previews match the viewer.
/// </summary>
public static class AutoLevels
{
    // Fluorescence (shown reverted, western-blot style): bands should be dark
    // and distinct; the background should be a clean light gray, not blank
    // white. The gain pulls the top of the reverted ramp down from 255.
    public const double FluoPLow = 1.0;     // noise floor
    public const double FluoPHigh = 99.9;   // just above band peaks
    public const double FluoGain = 0.92;    // non-blank background

    // Brightfield: a white film on a black board. The film should render near
    // 90% brightness (bright, but not pure white) so the markers stay visible.
    // The film level uses a high percentile (P98) that is always inside the
    // film region, even when the film only covers a small part of the frame;
    // a low percentile (P85) falls into the dark board for small films and
    // washes the film out to pure white.
    public const double BfPLow = 0.5;
    public const double BfFilmPct = 98.0;
    public const double BfTarget = 0.9;

    public static ulong[] Histogram(ushort[] px)
    {
        var h = new ulong[65536];
        foreach (ushort v in px) ++h[v];
        return h;
    }

    // numpy.percentile(..., 'linear') on a histogram — same as clxcpp.
    public static double Percentile(ulong[] hist, long n, double q)
    {
        if (n == 0) return 0;
        double idx = (n - 1) * q / 100.0;
        long lo = (long)Math.Floor(idx);
        long hi = (long)Math.Ceiling(idx);
        double frac = idx - Math.Floor(idx);
        double a = ValueAtRank(hist, lo);
        double b = ValueAtRank(hist, hi);
        return a + (b - a) * frac;
    }

    private static double ValueAtRank(ulong[] hist, long rank)
    {
        ulong cum = 0;
        for (int v = 0; v < hist.Length; v++)
        {
            cum += hist[v];
            if (cum > (ulong)rank) return v;
        }
        return hist.Length - 1;
    }

    public static Levels Fluo(ulong[] hist, long n, long dataMax)
    {
        double low = Percentile(hist, n, FluoPLow);
        double high = Percentile(hist, n, FluoPHigh);
        if (high > dataMax) high = dataMax;
        if (high <= low) high = low + 1;
        return new Levels(Math.Round(low), Math.Round(high), FluoGain);
    }

    public static Levels Brightfield(ulong[] hist, long n, long dataMax)
    {
        double low = Percentile(hist, n, BfPLow);
        double film = Percentile(hist, n, BfFilmPct);
        double high = film / BfTarget;
        if (high > dataMax) high = dataMax;
        if (high <= low) high = low + 1;
        return new Levels(Math.Round(low), Math.Round(high), 1.0);
    }
}
