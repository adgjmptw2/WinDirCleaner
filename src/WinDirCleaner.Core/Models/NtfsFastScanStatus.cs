namespace WinDirCleaner.Core.Models;

public enum NtfsFastScanStatus
{
    NotStarted = 0,
    Supported,
    NotNtfs,
    AccessDenied,
    ApiUnavailable,
    Failed,
    Completed,
}
