namespace WinDirCleaner.Core.Models;

public sealed class CleanupPreviewResult
{
    public CleanupPreviewResult(
        CleanupPreviewSummary summary,
        IReadOnlyList<CleanupPreviewTarget> sampleTargets,
        IReadOnlyList<string> messages)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        SampleTargets = sampleTargets ?? Array.Empty<CleanupPreviewTarget>();
        Messages = messages ?? Array.Empty<string>();
    }

    public CleanupPreviewSummary Summary { get; }

    public IReadOnlyList<CleanupPreviewTarget> SampleTargets { get; }

    public IReadOnlyList<string> Messages { get; }
}
