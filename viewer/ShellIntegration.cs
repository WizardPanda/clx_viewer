using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClxViewer;

/// <summary>
/// Registers the native thumbnail provider (ClinxThumbnailProvider.dll) as the
/// per-user COM server and .clx shell thumbnail handler. The viewer calls
/// <see cref="TryRegisterIfNeeded"/> on every normal launch so a portable build
/// behaves like an installed app without running a registration script.
/// </summary>
internal static class ShellIntegration
{
    private const string Clsid = "{6CF20D3A-B6A8-4DCB-8305-973E1B65E7B6}";
    private const string ThumbKey = "{E357FCCD-A995-4576-B01F-234630154E96}";
    private const string ProgId = "ClxViewer.Document";
    private const string Extension = ".clx";
    private const string BaseKey = @"Software\Classes";

    private const int SHCNE_ASSOCCHANGED = 0x08000000;

    public static string ExeDir => AppContext.BaseDirectory.TrimEnd('\\', '/');

    public static string DllPath => Path.Combine(ExeDir, "ClinxThumbnailProvider.dll");

    /// <summary>Registers shell integration if missing or stale (e.g. moved). Never throws.</summary>
    public static bool TryRegisterIfNeeded()
    {
        try
        {
            if (!File.Exists(DllPath)) return false;
            return RegisterCore();
        }
        catch
        {
            return false; // registration must never break the app
        }
    }

    /// <summary>Removes only the keys/values that point at our CLSID/ProgID. Never throws.</summary>
    public static bool TryUnregister()
    {
        try
        {
            return UnregisterCore();
        }
        catch
        {
            return false;
        }
    }

    private static bool RegisterCore()
    {
        bool changed = false;
        string exe = Path.Combine(ExeDir, "ClxViewer.exe");
        string dll = DllPath;

        using (var inproc = Registry.CurrentUser.CreateSubKey($@"{BaseKey}\CLSID\{Clsid}\InprocServer32"))
        {
            if (!string.Equals(inproc.GetValue(null) as string, dll, StringComparison.OrdinalIgnoreCase))
            {
                inproc.SetValue(null, dll, RegistryValueKind.String);
                changed = true;
            }
            if (!string.Equals(inproc.GetValue("ThreadingModel") as string, "Apartment", StringComparison.OrdinalIgnoreCase))
            {
                inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
                changed = true;
            }
        }

        // .clx shellex thumbnail handler (+ name alias)
        foreach (string handler in new[] { $@"{BaseKey}\{Extension}\shellex\{ThumbKey}", $@"{BaseKey}\{Extension}\shellex\ThumbnailHandler" })
        {
            using var h = Registry.CurrentUser.CreateSubKey(handler);
            if (!string.Equals(h.GetValue(null) as string, Clsid, StringComparison.OrdinalIgnoreCase))
            {
                h.SetValue(null, Clsid, RegistryValueKind.String);
                changed = true;
            }
        }

        // .clx association -> ClxViewer.Document
        using (var ext = Registry.CurrentUser.CreateSubKey($@"{BaseKey}\{Extension}"))
        {
            if (!string.Equals(ext.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase))
            {
                ext.SetValue(null, ProgId, RegistryValueKind.String);
                changed = true;
            }
        }
        Registry.CurrentUser.CreateSubKey($@"{BaseKey}\{Extension}\OpenWithProgids\{ProgId}");

        // ProgID + open command
        using (var prog = Registry.CurrentUser.CreateSubKey($@"{BaseKey}\{ProgId}"))
        {
            if ((prog.GetValue(null) as string) != "Clinx CLX Capture")
            {
                prog.SetValue(null, "Clinx CLX Capture", RegistryValueKind.String);
                changed = true;
            }
        }
        using (var cmd = Registry.CurrentUser.CreateSubKey($@"{BaseKey}\{ProgId}\shell\open\command"))
        {
            string value = $"\"{exe}\" \"%1\"";
            if (!string.Equals(cmd.GetValue(null) as string, value, StringComparison.OrdinalIgnoreCase))
            {
                cmd.SetValue(null, value, RegistryValueKind.String);
                changed = true;
            }
        }

        if (changed) NotifyAssocChanged();
        return changed;
    }

    private static bool UnregisterCore()
    {
        bool changed = false;

        // COM server for our CLSID only
        using (var root = Registry.CurrentUser.OpenSubKey(BaseKey, true))
        {
            if (root == null) return false;

            if (root.OpenSubKey($@"CLSID\{Clsid}") != null)
            {
                root.DeleteSubKeyTree($@"CLSID\{Clsid}", false);
                changed = true;
            }

            // shellex handlers that point at our CLSID
            foreach (string handler in new[] { $@"{Extension}\shellex\{ThumbKey}", $@"{Extension}\shellex\ThumbnailHandler" })
            {
                using var h = Registry.CurrentUser.OpenSubKey(handler, true);
                if (h != null && string.Equals(h.GetValue(null) as string, Clsid, StringComparison.OrdinalIgnoreCase))
                {
                    Registry.CurrentUser.DeleteSubKey(handler, false);
                    changed = true;
                }
            }

            // OpenWithProgids entry
            Registry.CurrentUser.DeleteSubKey($@"{BaseKey}\{Extension}\OpenWithProgids\{ProgId}", false);

            // .clx default association, only if it is ours
            bool extOurs;
            using (var ext = Registry.CurrentUser.OpenSubKey($@"{BaseKey}\{Extension}", true))
            {
                extOurs = ext != null && string.Equals(ext.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase);
            }
            if (extOurs)
            {
                root.DeleteSubKeyTree(Extension, false);
                changed = true;
            }

            // ProgID, only if the description still matches ours
            using (var prog = Registry.CurrentUser.OpenSubKey($@"{BaseKey}\{ProgId}", true))
            {
                if (prog != null && (prog.GetValue(null) as string) == "Clinx CLX Capture")
                {
                    root.DeleteSubKeyTree(ProgId, false);
                    changed = true;
                }
            }
        }

        if (changed) NotifyAssocChanged();
        return changed;
    }

    private static void NotifyAssocChanged()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // ignore
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
