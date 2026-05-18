namespace WinDirCleaner.Core.Models;

public sealed class CleanupPreviewSummary
{
    public CleanupPreviewSummary(
        int selectedCandidateCount,
        int scannedCandidateCount,
        int skippedCandidateCount,
        int targetFileCount,
        int targetDirectoryCount,
        int inaccessibleCount,
        int failedCount,
        long estimatedBytes,
        TimeSpan elapsed)
    {
        SelectedCandidateCount = ClampNonNegative(selectedCandidateCount);
        ScannedCandidateCount = ClampNonNegative(scannedCandidateCount);
        SkippedCandidateCount = ClampNonNegative(skippedCandidateCount);
        TargetFileCount = ClampNonNegative(targetFileCount);
        TargetDirectoryCount = ClampNonNegative(targetDirectoryCount);
        InaccessibleCount = ClampNonNegative(inaccessibleCount);
        FailedCount = ClampNonNegative(failedCount);
        EstimatedBytes = estimatedBytes < 0 ? 0 : estimatedBytes;
        Elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public int SelectedCandidateCount { get; }

    public int ScannedCandidateCount { get; }

    public int SkippedCandidateCount { get; }

    public int TargetFileCount { get; }

    public int TargetDirectoryCount { get; }

    public int InaccessibleCount { get; }

    public int FailedCount { get; }

    public long EstimatedBytes { get; }

    public TimeSpan Elapsed { get; }

    public bool HasTargets => TargetFileCount > 0 || TargetDirectoryCount > 0;

    public double FilesPerSecond
    {
        get
        {
            var sec = Elapsed.TotalSeconds;
            return sec > 0 ? TargetFileCount / sec : 0;
        }
    }

    private static int ClampNonNegative(int v) => v < 0 ? 0 : v;
}
