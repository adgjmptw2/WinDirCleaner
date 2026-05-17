namespace WinDirCleaner.Core.Models;

public sealed class StorageAnalysisPerformanceSummary
{
    public required int PlannedTopLevelItemCount { get; init; }
    public required int CompletedTopLevelItemCount { get; init; }

    public required int InaccessibleTopLevelCount { get; init; }

    public required long TotalFilesScanned { get; init; }

    public required long TotalDirectoriesScanned { get; init; }

    public required long TotalBytesScanned { get; init; }

    public required TimeSpan TotalElapsed { get; init; }

    public StorageAnalysisMode AnalysisMode { get; init; } = StorageAnalysisMode.Sequential;

    public int MaxDegreeOfParallelism { get; init; } = StorageAnalysisOptions.DefaultMaxDegreeOfParallelism;

    public double FilesPerSecond => RatePerSecond(TotalFilesScanned, TotalElapsed);

    public double DirectoriesPerSecond => RatePerSecond(TotalDirectoriesScanned, TotalElapsed);

    public double BytesPerSecond => RatePerSecond(TotalBytesScanned, TotalElapsed);

    public static StorageAnalysisPerformanceSummary Create(
        IReadOnlyList<StorageAnalysisItem> topLevelResultsSoFar,
        long totalFilesScanned,
        long totalDirectoriesScanned,
        long totalBytesScanned,
        TimeSpan totalElapsed,
        int? plannedTopLevelCount = null,
        StorageAnalysisMode analysisMode = StorageAnalysisMode.Sequential,
        int maxDegreeOfParallelism = StorageAnalysisOptions.DefaultMaxDegreeOfParallelism)
    {
        var inaccessible = 0;
        foreach (var r in topLevelResultsSoFar)
        {
            if (!r.IsAccessible)
            {
                inaccessible++;
            }
        }

        var completed = topLevelResultsSoFar.Count;
        var planned = plannedTopLevelCount ?? completed;

        return new StorageAnalysisPerformanceSummary
        {
            PlannedTopLevelItemCount = planned,
            CompletedTopLevelItemCount = completed,
            InaccessibleTopLevelCount = inaccessible,
            TotalFilesScanned = totalFilesScanned,
            TotalDirectoriesScanned = totalDirectoriesScanned,
            TotalBytesScanned = totalBytesScanned,
            TotalElapsed = totalElapsed,
            AnalysisMode = analysisMode,
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
        };
    }

    private static double RatePerSecond(long amount, TimeSpan elapsed)
    {
        var sec = elapsed.TotalSeconds;
        if (sec <= 0 || amount <= 0)
        {
            return 0;
        }

        return amount / sec;
    }
}
