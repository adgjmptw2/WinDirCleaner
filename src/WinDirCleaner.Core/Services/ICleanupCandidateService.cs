using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

/// <summary>
/// 정리 후보 목록을 제공합니다. 프리뷰 전용이며 파일 시스템을 변경하지 않습니다.
/// </summary>
public interface ICleanupCandidateService
{
    IReadOnlyList<CleanupItem> GetPreviewCandidates();
}
