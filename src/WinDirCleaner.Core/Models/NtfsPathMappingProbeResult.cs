namespace WinDirCleaner.Core.Models;

public sealed class NtfsPathMappingProbeResult
{
    public NtfsPathMappingProbeResult(
        NtfsPathMappingStatus status,
        string inputPath,
        string rootPath,
        string volumePath,
        bool isNtfs,
        long recordsScanned,
        long parsedRecords,
        TimeSpan elapsed,
        string? matchedFileReferenceNumber = null,
        string? matchedName = null,
        string? errorMessage = null,
        string? detailMessage = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("InputPath is required.", nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("RootPath is required.", nameof(rootPath));
        }

        if (string.IsNullOrWhiteSpace(volumePath))
        {
            throw new ArgumentException("VolumePath is required.", nameof(volumePath));
        }

        if (recordsScanned < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recordsScanned), recordsScanned, "RecordsScanned cannot be negative.");
        }

        if (parsedRecords < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parsedRecords), parsedRecords, "ParsedRecords cannot be negative.");
        }

        Status = status;
        InputPath = inputPath.Trim();
        RootPath = rootPath.Trim();
        VolumePath = volumePath.Trim();
        IsNtfs = isNtfs;
        RecordsScanned = recordsScanned;
        ParsedRecords = parsedRecords;
        Elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        MatchedFileReferenceNumber = matchedFileReferenceNumber;
        MatchedName = matchedName;
        ErrorMessage = errorMessage;
        DetailMessage = detailMessage;
    }

    public NtfsPathMappingStatus Status { get; }

    public string InputPath { get; }

    public string RootPath { get; }

    public string VolumePath { get; }

    public bool IsNtfs { get; }

    public string? MatchedFileReferenceNumber { get; }

    public string? MatchedName { get; }

    public long RecordsScanned { get; }

    public long ParsedRecords { get; }

    public TimeSpan Elapsed { get; }

    public string? ErrorMessage { get; }

    public string? DetailMessage { get; }
}
