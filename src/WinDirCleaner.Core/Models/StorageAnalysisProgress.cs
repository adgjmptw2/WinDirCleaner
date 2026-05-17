namespace WinDirCleaner.Core.Models;

public sealed class StorageAnalysisProgress
{
    public StorageAnalysisProgressKind Kind { get; init; }

    public string RootPath { get; init; } = string.Empty;

    public string? CurrentPath { get; init; }

    public string? CurrentTopLevelName { get; init; }

    public int TotalTopLevelItems { get; init; }

    public int CompletedTopLevelItems { get; init; }

    public long FilesScanned { get; init; }

    public long DirectoriesScanned { get; init; }

    public long BytesScanned { get; init; }

    public TimeSpan Elapsed { get; init; }

    public StorageAnalysisItem? CompletedItem { get; init; }
    public StorageAnalysisPerformanceSummary? PerformanceSummary { get; init; }

    public StorageAnalysisMode AnalysisMode { get; init; } = StorageAnalysisMode.Sequential;
    public int MaxDegreeOfParallelism { get; init; } = StorageAnalysisOptions.DefaultMaxDegreeOfParallelism;

    public string? Message { get; init; }

    public double TopLevelProgressPercent =>
        TotalTopLevelItems <= 0
            ? 0
            : Math.Min(100.0, (double)CompletedTopLevelItems / TotalTopLevelItems * 100.0);
}
