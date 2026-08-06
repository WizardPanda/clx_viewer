using System.Runtime.InteropServices;
using System.Text;

namespace ClxViewer.Services;

/// <summary>
/// Thin C-ABI bridge to clxreader.dll (native, linked against clxcpp).
/// Keeps parsing pixel-identical to the tested C++ library.
/// </summary>
internal static class ClxReaderNative
{
    private const string Dll = "clxreader.dll";

    [StructLayout(LayoutKind.Sequential)]
    public struct ClxrImageInfo
    {
        public long Index;
        public long Offset;
        public long Type;
        public long Width;
        public long Height;
        public long BitsPerSample;
        public long MinValue;
        public long MaxValue;
        public long ByteCount;
        public int Channel;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr clxr_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void clxr_close(IntPtr f);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int clxr_image_count(IntPtr f);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int clxr_image_info_get(IntPtr f, int i, out ClxrImageInfo info);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int clxr_copy_image_pixels(IntPtr f, int i, byte[] dst, long dstCap);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int clxr_metadata_json(IntPtr f, byte[]? buf, int len);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int clxr_stem(IntPtr f, byte[]? buf, int len);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void clxr_last_error(byte[] buf, int len);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int clxr_save_tiff(IntPtr f, int i, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    public static string MetadataJson(IntPtr f)
    {
        int n = clxr_metadata_json(f, null, 0);
        if (n <= 0) return "{}";
        byte[] buf = new byte[n + 1];
        clxr_metadata_json(f, buf, buf.Length);
        return Encoding.UTF8.GetString(buf).TrimEnd('\0');
    }

    public static string Stem(IntPtr f)
    {
        int n = clxr_stem(f, null, 0);
        if (n <= 0) return "";
        byte[] buf = new byte[n + 1];
        clxr_stem(f, buf, buf.Length);
        return Encoding.UTF8.GetString(buf).TrimEnd('\0');
    }

    public static string LastError()
    {
        byte[] buf = new byte[1024];
        clxr_last_error(buf, buf.Length);
        return Encoding.UTF8.GetString(buf).TrimEnd('\0');
    }
}
