namespace WinDirCleaner.Core.Models;

public sealed class NtfsFastScanTreeSummary
{
    public NtfsFastScanTreeSummary(
        long totalRecords,
        long parsedRecords,
        long fileRecords,
        long directoryRecords,
        long reparsePointRecords,
        long unsupportedVersionRecords,
        long invalidRecords,
        long linkedRecords,
        long orphanRecords,
        long rootCandidateRecords,
        TimeSpan elapsed)
    {
        ValidateNonNegative(nameof(totalRecords), totalRecords);
        ValidateNonNegative(nameof(parsedRecords), parsedRecords);
        ValidateNonNegative(nameof(fileRecords), fileRecords);
        ValidateNonNegative(nameof(directoryRecords), directoryRecords);
        ValidateNonNegative(nameof(reparsePointRecords), reparsePointRecords);
        ValidateNonNegative(nameof(unsupportedVersionRecords), unsupportedVersionRecords);
        ValidateNonNegative(nameof(invalidRecords), invalidRecords);
        ValidateNonNegative(nameof(linkedRecords), linkedRecords);
        ValidateNonNegative(nameof(orphanRecords), orphanRecords);
        ValidateNonNegative(nameof(rootCandidateRecords), rootCandidateRecords);

        TotalRecords = totalRecords;
        ParsedRecords = parsedRecords;
        FileRecords = fileRecords;
        DirectoryRecords = directoryRecords;
        ReparsePointRecords = reparsePointRecords;
        UnsupportedVersionRecords = unsupportedVersionRecords;
        InvalidRecords = invalidRecords;
        LinkedRecords = linkedRecords;
        OrphanRecords = orphanRecords;
        RootCandidateRecords = rootCandidateRecords;
        Elapsed = elapsed;
    }

    public long TotalRecords { get; }

    public long ParsedRecords { get; }

    public long FileRecords { get; }

    public long DirectoryRecords { get; }

    public long ReparsePointRecords { get; }

    public long UnsupportedVersionRecords { get; }

    public long InvalidRecords { get; }

    public long LinkedRecords { get; }

    public long OrphanRecords { get; }

    public long RootCandidateRecords { get; }

    public TimeSpan Elapsed { get; }

    private static void ValidateNonNegative(string name, long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value cannot be negative.");
        }
    }
}
