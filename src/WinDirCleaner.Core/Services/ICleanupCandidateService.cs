using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

/// <summary>
/// 정리 후보 목록을 제공합니다. 프리뷰 전용이며 파일 시스템을 변경하지 않습니다.
/// </summary>
public interface ICleanupCandidateService
{
    IReadOnlyList<CleanupItem> GetPreviewCandidates();

    /// <summary>
    /// 프리뷰 서비스는 정적 목록을 그대로 반환합니다. 실제 탐지는 <see cref="CleanupCandidateDetectionService"/>를 사용하세요.
    /// </summary>
    Task<IReadOnlyList<CleanupItem>> DetectCandidatesAsync(CancellationToken cancellationToken = default);
}
