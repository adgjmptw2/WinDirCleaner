using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

public interface INtfsFastScanProbeService
{
    Task<NtfsFastScanProbeResult> ProbeAsync(string rootPath, CancellationToken cancellationToken = default);
}
