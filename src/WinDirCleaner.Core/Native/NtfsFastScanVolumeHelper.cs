using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinDirCleaner.Core.Native;

internal static class NtfsFastScanVolumeHelper
{
    internal static string NormalizeRootDirectory(string rootPath)
    {
        var trimmed = rootPath.Trim();
        if (trimmed.Length == 2 && trimmed[1] == ':')
        {
            trimmed = string.Concat(trimmed, Path.DirectorySeparatorChar);
        }
        else
        {
            trimmed = Path.GetFullPath(trimmed);
        }

        if (!trimmed.EndsWith(Path.DirectorySeparatorChar) && !trimmed.EndsWith(Path.AltDirectorySeparatorChar))
        {
            trimmed += Path.DirectorySeparatorChar;
        }

        if (!Directory.Exists(trimmed))
        {
            throw new DirectoryNotFoundException($"Directory not found: {trimmed}");
        }

        return trimmed;
    }

    internal static SafeFileHandle? OpenVolumeReadOnly(string volumePath)
    {
        // Broad share so OpenFileById against the same volume is less likely to fail while we only read metadata.
        var share = WindowsNativeMethods.FileShareRead |
                    WindowsNativeMethods.FileShareWrite |
                    WindowsNativeMethods.FileShareDelete;

        var handle = WindowsNativeMethods.CreateFileW(
            volumePath,
            WindowsNativeMethods.GenericRead,
            share,
            IntPtr.Zero,
            WindowsNativeMethods.OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return handle;
    }
}
