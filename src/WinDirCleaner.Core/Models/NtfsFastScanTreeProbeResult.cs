namespace WinDirCleaner.Core.Models;

public sealed class NtfsFastScanTreeProbeResult
{
    public NtfsFastScanTreeProbeResult(
        NtfsFastScanStatus status,
        string rootPath,
        string volumePath,
        bool isNtfs,
        NtfsFastScanTreeSummary summary,
        IReadOnlyList<NtfsFileRecord>? sampleRecords = null,
        IReadOnlyList<string>? sampleRootNames = null,
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

        Status = status;
        RootPath = rootPath.Trim();
        VolumePath = volumePath.Trim();
        IsNtfs = isNtfs;
        Summary = summary;
        SampleRecords = sampleRecords ?? Array.Empty<NtfsFileRecord>();
        SampleRootNames = sampleRootNames ?? Array.Empty<string>();
        ErrorMessage = errorMessage;
        DetailMessage = detailMessage;
    }

    public NtfsFastScanStatus Status { get; }

    public string RootPath { get; }

    public string VolumePath { get; }

    public bool IsNtfs { get; }

    public NtfsFastScanTreeSummary Summary { get; }

    public IReadOnlyList<NtfsFileRecord> SampleRecords { get; }

    public IReadOnlyList<string> SampleRootNames { get; }

    public string? ErrorMessage { get; }

    public string? DetailMessage { get; }
}
