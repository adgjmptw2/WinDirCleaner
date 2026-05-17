using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

public interface IStorageAnalysisService
{
    Task<IReadOnlyList<StorageAnalysisItem>> AnalyzeTopLevelAsync(
        string rootPath,
        StorageAnalysisOptions? options = null,
        IProgress<StorageAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
