using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

/// <summary>
/// 일부 안전한 후보 경로만 읽기 전용으로 크기를 계산합니다. 위험 분류 경로는 크기를 계산하지 않습니다.
/// </summary>
public sealed class CleanupCandidateDetectionService
{
    private readonly IReadOnlyDirectorySizeService _sizeService;

    public CleanupCandidateDetectionService(IReadOnlyDirectorySizeService? sizeService = null) =>
        _sizeService = sizeService ?? new ReadOnlyDirectorySizeService();

    public async Task<IReadOnlyList<CleanupItem>> DetectCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windows))
        {
            windows = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "Windows");
        }

        windows = windows.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var tempPath = Path.GetTempPath();
        var wuDownload = Path.Combine(windows, "SoftwareDistribution", "Download");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var crashDumps = Path.Combine(string.IsNullOrEmpty(localAppData) ? windows : localAppData, "CrashDumps");
        var minidump = Path.Combine(windows, "Minidump");
        var winsxs = Path.Combine(windows, "WinSxS");
        var driverStore = Path.Combine(windows, "System32", "DriverStore");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrEmpty(programFiles))
        {
            programFiles = Path.Combine(Path.GetPathRoot(windows) ?? "C:\\", "Program Files");
        }

        var tempSize = await _sizeService.CalculateSizeAsync(tempPath, cancellationToken).ConfigureAwait(false);
        var wuSize = await _sizeService.CalculateSizeAsync(wuDownload, cancellationToken).ConfigureAwait(false);
        var crashSize = await _sizeService.CalculateSizeAsync(crashDumps, cancellationToken).ConfigureAwait(false);
        var miniSize = await _sizeService.CalculateSizeAsync(minidump, cancellationToken).ConfigureAwait(false);

        return new[]
        {
            new CleanupItem(
                id: "detect-user-temp",
                name: "사용자 임시 파일",
                path: tempPath,
                sizeBytes: tempSize,
                risk: CleanupRisk.Recommended,
                selected: false,
                description: "앱과 설치 프로그램이 만든 임시 파일이 쌓일 수 있는 위치입니다.",
                reason: "일반적으로 정리 후보가 될 수 있는 범주로 분류됩니다.",
                impact: "실행 중인 앱이 사용 중인 임시 파일은 집계에서 제외되거나 크기가 달라 보일 수 있습니다."),
            new CleanupItem(
                id: "detect-wu-download-cache",
                name: "Windows 업데이트 다운로드 캐시",
                path: wuDownload,
                sizeBytes: wuSize,
                risk: CleanupRisk.Recommended,
                selected: false,
                description: "Windows 업데이트 다운로드 파일이 남을 수 있는 위치입니다.",
                reason: "업데이트 후 남은 다운로드 캐시는 정리 후보가 될 수 있습니다.",
                impact: "이후 업데이트 확인 시 필요한 파일이 다시 내려받아질 수 있습니다."),
            new CleanupItem(
                id: "detect-local-crashdumps",
                name: "사용자 크래시 덤프",
                path: crashDumps,
                sizeBytes: crashSize,
                risk: CleanupRisk.Optional,
                selected: false,
                description: "앱 크래시 분석용 덤프 파일이 모일 수 있는 위치입니다.",
                reason: "문제 분석에 필요할 수 있어 사용자 판단이 필요합니다.",
                impact: "원인 분석에 필요한 정보가 될 수 있어 임의로 비우기 어렵습니다."),
            new CleanupItem(
                id: "detect-minidump",
                name: "Windows 미니덤프",
                path: minidump,
                sizeBytes: miniSize,
                risk: CleanupRisk.Optional,
                selected: false,
                description: "시스템 오류 분석용 미니덤프가 모일 수 있는 위치입니다.",
                reason: "블루스크린 등 분석에 필요할 수 있어 사용자 판단이 필요합니다.",
                impact: "접근이 제한되면 크기가 0으로 보일 수 있습니다."),
            new CleanupItem(
                id: "detect-winsxs",
                name: "WinSxS(Windows Side-by-Side)",
                path: winsxs,
                sizeBytes: 0,
                risk: CleanupRisk.Dangerous,
                selected: false,
                description: "Windows 구성 요소 저장소입니다.",
                reason: "직접 삭제하면 시스템 구성 요소 복구·업데이트에 문제가 생길 수 있습니다.",
                impact: "이 앱에서는 크기를 계산하지 않으며 삭제하거나 선택할 수 없습니다."),
            new CleanupItem(
                id: "detect-driverstore",
                name: "DriverStore(드라이버 저장소)",
                path: driverStore,
                sizeBytes: 0,
                risk: CleanupRisk.Dangerous,
                selected: false,
                description: "Windows 드라이버 저장소입니다.",
                reason: "직접 삭제하면 드라이버 복구·장치 인식에 문제가 생길 수 있습니다.",
                impact: "이 앱에서는 크기를 계산하지 않으며 삭제하거나 선택할 수 없습니다."),
            new CleanupItem(
                id: "detect-program-files",
                name: "Program Files(설치 프로그램)",
                path: programFiles,
                sizeBytes: 0,
                risk: CleanupRisk.Dangerous,
                selected: false,
                description: "설치된 프로그램 본체가 들어 있는 위치입니다.",
                reason: "직접 삭제하면 프로그램이 손상되거나 제거될 수 있습니다.",
                impact: "이 앱에서는 크기를 계산하지 않으며 삭제하거나 선택할 수 없습니다."),
        };
    }
}
