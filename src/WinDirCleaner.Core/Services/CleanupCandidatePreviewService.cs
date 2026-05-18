using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

/// <summary>
/// 실제 스캔 없이 정리 후보 UI를 점검하기 위한 정적 프리뷰 목록을 반환합니다.
/// </summary>
public sealed class CleanupCandidatePreviewService : ICleanupCandidateService
{
    public IReadOnlyList<CleanupItem> GetPreviewCandidates() =>
        new[]
        {
            new CleanupItem(
                id: "preview-user-temp",
                name: "사용자 임시 파일",
                path: "%TEMP%",
                sizeBytes: 128L * 1024 * 1024,
                risk: CleanupRisk.Recommended,
                selected: false,
                description: "앱과 설치 프로그램이 만든 임시 파일이 쌓일 수 있는 위치입니다.",
                reason: "일반적으로 정리 후보가 될 수 있는 범주로 분류됩니다.",
                impact: "일부 실행 중인 앱의 임시 파일은 다른 프로세스가 사용 중이라 비워지지 않을 수 있습니다."),
            new CleanupItem(
                id: "preview-wu-download-cache",
                name: "Windows 업데이트 다운로드 캐시",
                path: @"C:\Windows\SoftwareDistribution\Download",
                sizeBytes: 256L * 1024 * 1024,
                risk: CleanupRisk.Recommended,
                selected: false,
                description: "Windows 업데이트 다운로드 파일이 남을 수 있는 위치입니다.",
                reason: "업데이트 후 남은 다운로드 캐시는 정리 후보가 될 수 있습니다.",
                impact: "다음 업데이트 확인 시 필요한 파일이 다시 내려받아질 수 있습니다."),
            new CleanupItem(
                id: "preview-crash-dumps",
                name: "크래시 덤프 파일",
                path: @"C:\Windows\Minidump",
                sizeBytes: 64L * 1024 * 1024,
                risk: CleanupRisk.Optional,
                selected: false,
                description: "앱이나 시스템 오류 분석에 쓰이는 덤프 파일이 모일 수 있습니다.",
                reason: "문제 분석에 필요할 수 있어 사용자 판단이 필요합니다.",
                impact: "삭제하면 원인 분석에 필요한 정보가 사라질 수 있습니다."),
            new CleanupItem(
                id: "preview-downloads-large",
                name: "다운로드 폴더의 큰 파일",
                path: "%USERPROFILE%\\Downloads",
                sizeBytes: 0,
                risk: CleanupRisk.Optional,
                selected: false,
                description: "사용자가 직접 받은 파일이 모이는 위치입니다.",
                reason: "개인 문서·설치 파일이 섞일 수 있어 자동 정리 대상으로 두기 어렵습니다.",
                impact: "필요한 설치 미디어나 개인 자료를 잃지 않도록 직접 확인이 필요합니다."),
            new CleanupItem(
                id: "preview-winsxs",
                name: "WinSxS(Windows Side-by-Side)",
                path: @"C:\Windows\WinSxS",
                sizeBytes: 0,
                risk: CleanupRisk.Dangerous,
                selected: false,
                description: "Windows 구성 요소 저장소입니다.",
                reason: "직접 삭제하면 시스템 구성 요소 복구·업데이트에 문제가 생길 수 있습니다.",
                impact: "이 앱에서는 삭제하거나 선택할 수 없도록 막아 두었습니다."),
            new CleanupItem(
                id: "preview-driverstore",
                name: "DriverStore(드라이버 저장소)",
                path: @"C:\Windows\System32\DriverStore\FileRepository",
                sizeBytes: 0,
                risk: CleanupRisk.Dangerous,
                selected: false,
                description: "Windows 드라이버 저장소입니다.",
                reason: "직접 삭제하면 드라이버 복구·장치 인식에 문제가 생길 수 있습니다.",
                impact: "이 앱에서는 삭제하거나 선택할 수 없도록 막아 두었습니다."),
            new CleanupItem(
                id: "preview-program-files",
                name: "Program Files(설치 프로그램)",
                path: @"C:\Program Files",
                sizeBytes: 0,
                risk: CleanupRisk.Dangerous,
                selected: false,
                description: "설치된 프로그램 본체가 들어 있는 위치입니다.",
                reason: "직접 삭제하면 프로그램이 손상되거나 제거될 수 있습니다.",
                impact: "이 앱에서는 삭제하거나 선택할 수 없도록 막아 두었습니다."),
        };

    public Task<IReadOnlyList<CleanupItem>> DetectCandidatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetPreviewCandidates());
    }
}
