using System;
using System.Runtime.InteropServices;

namespace ClxViewer.Services;

/// <summary>
/// Modern (Vista+) folder picker via the Common Item Dialog (IFileOpenDialog),
/// replacing the .NET 8-only Microsoft.Win32.OpenFolderDialog.
/// </summary>
internal static class ModernFolderPicker
{
    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialog
    {
    }

    [ComImport, Guid("42f85136-db7a-439c-85f1-e4075d135fc8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr hwndOwner);
        [PreserveSig] int SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        [PreserveSig] int SetFileTypeIndex(uint iFileType);
        [PreserveSig] int GetFileTypeIndex(out uint piFileType);
        [PreserveSig] int Advise(IntPtr pfde, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOptions(uint fos);
        [PreserveSig] int GetOptions(out uint pfos);
        [PreserveSig] int SetDefaultFolder(IntPtr psi);
        [PreserveSig] int SetFolder(IntPtr psi);
        [PreserveSig] int GetFolder(out IntPtr ppsi);
        [PreserveSig] int GetCurrentSelection(out IntPtr ppsi);
        [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        [PreserveSig] int GetResult(out IntPtr ppsi);
        [PreserveSig] int GetResults(IntPtr ppenum);
        [PreserveSig] int GetSelectedItems(IntPtr ppsai);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IntPtr ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IntPtr psi, uint hint, out int piOrder);
    }

    /// <summary>Shows the folder picker. Returns the chosen folder path or null if cancelled.</summary>
    public static string? PickFolder(IntPtr ownerHwnd, string title)
    {
        try
        {
            var dialog = new FileOpenDialog();
            var ifd = (IFileOpenDialog)dialog;
            ifd.SetOptions(FOS_PICKFOLDERS);
            ifd.SetTitle(title);
            int hr = ifd.Show(ownerHwnd);
            if (hr != 0) return null;  // cancelled or failed

            IntPtr pItem;
            if (ifd.GetResult(out pItem) != 0 || pItem == IntPtr.Zero) return null;
            try
            {
                var item = (IShellItem)Marshal.GetObjectForIUnknown(pItem);
                return item.GetDisplayName(SIGDN_FILESYSPATH, out string name) == 0 ? name : null;
            }
            finally
            {
                Marshal.Release(pItem);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
