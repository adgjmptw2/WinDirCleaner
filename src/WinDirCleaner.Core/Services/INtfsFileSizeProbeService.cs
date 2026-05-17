using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

public interface INtfsFileSizeProbeService
{
    /// <summary>USN에서 고른 파일 일부에 대해 OpenFileById로 크기 조회를 시도합니다. sampleCount는 1~50,000으로 클램프됩니다.</summary>
    Task<NtfsFileSizeProbeResult> ProbeFileSizesAsync(
        string rootPath,
        int sampleCount = 500,
        CancellationToken cancellationToken = default);
}
