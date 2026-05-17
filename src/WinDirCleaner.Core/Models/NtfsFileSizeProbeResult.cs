namespace WinDirCleaner.Core.Models;

public sealed class NtfsFileSizeProbeResult
{
    public NtfsFileSizeProbeResult(
        NtfsFastScanStatus status,
        string rootPath,
        string volumePath,
        bool isNtfs,
        NtfsFileSizeProbeSummary summary,
        IReadOnlyList<NtfsFileSizeProbeSample>? samples = null,
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
        Samples = samples ?? Array.Empty<NtfsFileSizeProbeSample>();
        ErrorMessage = errorMessage;
        DetailMessage = detailMessage;
    }

    public NtfsFastScanStatus Status { get; }

    public string RootPath { get; }

    public string VolumePath { get; }

    public bool IsNtfs { get; }

    public NtfsFileSizeProbeSummary Summary { get; }

    public IReadOnlyList<NtfsFileSizeProbeSample> Samples { get; }

    public string? ErrorMessage { get; }

    public string? DetailMessage { get; }
}
