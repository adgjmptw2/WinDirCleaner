using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

public interface ICleanupPreviewService
{
    Task<CleanupPreviewResult> PreviewAsync(
        IReadOnlyList<CleanupItem> candidates,
        CancellationToken cancellationToken = default);
}
