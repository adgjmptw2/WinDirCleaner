using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

public interface INtfsFastScanTreeProbeService
{
    Task<NtfsFastScanTreeProbeResult> ProbeTreeAsync(string rootPath, CancellationToken cancellationToken = default);
}
