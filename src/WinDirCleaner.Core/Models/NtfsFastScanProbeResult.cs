namespace WinDirCleaner.Core.Models;

public sealed class NtfsFastScanProbeResult
{
    public NtfsFastScanProbeResult(
        NtfsFastScanStatus status,
        string rootPath,
        string volumePath,
        bool isNtfs,
        long recordsRead,
        TimeSpan elapsed,
        string? errorMessage = null,
        string? detailMessage = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        if (string.IsNullOrWhiteSpace(volumePath))
        {
            throw new ArgumentException("Volume path is required.", nameof(volumePath));
        }

        if (recordsRead < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recordsRead), recordsRead, "RecordsRead cannot be negative.");
        }

        Status = status;
        RootPath = rootPath.Trim();
        VolumePath = volumePath.Trim();
        IsNtfs = isNtfs;
        RecordsRead = recordsRead;
        Elapsed = elapsed;
        ErrorMessage = errorMessage;
        DetailMessage = detailMessage;
    }

    public NtfsFastScanStatus Status { get; }

    public string RootPath { get; }

    public string VolumePath { get; }

    public bool IsNtfs { get; }

    public long RecordsRead { get; }

    public TimeSpan Elapsed { get; }

    public string? ErrorMessage { get; }

    public string? DetailMessage { get; }
}
