namespace ClxViewer.Services;

public static class Renderer
{
    /// <summary>
    /// Map raw 16-bit pixels to 8-bit grayscale.
    /// reverted=true: bright signal -> dark (western blot), background -> light.
    /// gain compresses the bright end (1.0 = exact mapping, 0.92 = auto fluo).
    /// </summary>
    public static byte[] Map(ushort[] px, double low, double high, bool reverted, double gain)
    {
        double range = high - low;
        if (range <= 0) range = 1;
        double scale = 255.0 / range * gain;
        var outB = new byte[px.Length];
        if (reverted)
        {
            for (int i = 0; i < px.Length; i++)
            {
                double v = (high - px[i]) * scale;
                outB[i] = v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)Math.Round(v);
            }
        }
        else
        {
            for (int i = 0; i < px.Length; i++)
            {
                double v = (px[i] - low) * scale;
                outB[i] = v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)Math.Round(v);
            }
        }
        return outB;
    }

    /// <summary>
    /// Merged view: subtract the fluorescence signal from the brightfield, so
    /// bands appear as dark marks on the bright film rather than brighter
    /// additions.
    /// </summary>
    public static byte[] Merge(byte[] bf, byte[] fluo)
    {
        var outB = new byte[bf.Length];
        for (int i = 0; i < bf.Length; i++)
        {
            int v = bf[i] - fluo[i];
            outB[i] = v < 0 ? (byte)0 : (byte)v;
        }
        return outB;
    }
}
