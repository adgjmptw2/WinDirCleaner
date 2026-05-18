using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

public interface INtfsPathMappingProbeService
{
    Task<NtfsPathMappingProbeResult> ProbePathMappingAsync(
        string targetPath,
        CancellationToken cancellationToken = default);
}
