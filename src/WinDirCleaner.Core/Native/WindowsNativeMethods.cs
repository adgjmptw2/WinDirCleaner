using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinDirCleaner.Core.Native;

internal static class WindowsNativeMethods
{
    internal const uint GenericRead = 0x80000000;

    internal const uint FileShareRead = 0x00000001;

    internal const uint FileShareWrite = 0x00000002;

    internal const uint FileShareDelete = 0x00000004;

    internal const uint OpenExisting = 3;
    internal const uint FsctlEnumUsnData = 0x000900b3;
    [StructLayout(LayoutKind.Sequential)]
    internal struct MftEnumDataV0
    {
        internal ulong StartFileReferenceNumber;

        internal long LowUsn;

        internal long HighUsn;
    }

    [DllImport("kernel32.dll", BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref MftEnumDataV0 lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    internal const int FileIdTypeFileId = 0;

    internal const uint FileReadAttributes = 0x00000080;

    // FILE_ID_DESCRIPTOR, FileId branch; explicit offsets, 24 bytes on 64-bit Windows.
    internal const uint FileIdDescriptorSizeBytes = 24;

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct FileIdDescriptor
    {
        [FieldOffset(0)]
        internal uint dwSize;

        [FieldOffset(4)]
        internal int Type;

        [FieldOffset(8)]
        internal long FileId;
    }

    [DllImport("kernel32.dll", BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle OpenFileById(
        SafeFileHandle hVolumeHint,
        ref FileIdDescriptor lpFileId,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwFlagsAndAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileSizeEx(SafeFileHandle hFile, out long fileSize);
}
