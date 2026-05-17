using WinDirCleaner.Core.Models;

namespace WinDirCleaner.App.Services;

/// <summary>
/// 스크린샷·포트폴리오용 정적 샘플. 파일 시스템·분석·NTFS 서비스는 호출하지 않습니다.
/// </summary>
internal static class DemoDataService
{
    private const long GiB = 1024L * 1024L * 1024L;

    private const string DemoRootC = @"C:\demo-layout";
    private const string DemoRootD = @"D:\demo-layout";

    internal static IReadOnlyList<DriveSummary> GetDemoDriveSummaries() =>
        new[]
        {
            new DriveSummary(
                @"C:\",
                "Demo System",
                totalBytes: 256L * GiB,
                usedBytes: 160L * GiB,
                freeBytes: 96L * GiB),
            new DriveSummary(
                @"D:\",
                "Demo Data",
                totalBytes: 1024L * GiB,
                usedBytes: 640L * GiB,
                freeBytes: 384L * GiB),
        };

    internal static IReadOnlyList<StorageAnalysisItem> GetDemoTopLevelItems() =>
        new[]
        {
            NewDemoDir("User Files", $@"{DemoRootC}\user-files", 48L * GiB, 18_200, 420, TimeSpan.FromMilliseconds(240)),
            NewDemoDir("Applications", $@"{DemoRootC}\applications", 72L * GiB, 52_000, 1_100, TimeSpan.FromMilliseconds(380)),
            NewDemoDir("Downloads", $@"{DemoRootC}\downloads", 12L * GiB, 6_400, 80, TimeSpan.FromMilliseconds(110)),
            NewDemoDir("Media", $@"{DemoRootC}\media", 36L * GiB, 9_800, 260, TimeSpan.FromMilliseconds(190)),
            NewDemoDir("Project Cache", $@"{DemoRootC}\project-cache", 8L * GiB, 120_000, 2_400, TimeSpan.FromMilliseconds(510)),
            NewDemoDir("Demo Samples", $@"{DemoRootC}\demo-samples", 2L * GiB, 900, 40, TimeSpan.FromMilliseconds(45)),
        };

    internal static IReadOnlyList<StorageAnalysisItem> GetDemoTopLevelItemsForDriveD() =>
        new[]
        {
            NewDemoDir("Archive Set A", $@"{DemoRootD}\archive-a", 200L * GiB, 4_200, 180, TimeSpan.FromMilliseconds(160)),
            NewDemoDir("Archive Set B", $@"{DemoRootD}\archive-b", 180L * GiB, 3_800, 140, TimeSpan.FromMilliseconds(150)),
            NewDemoDir("Demo Samples", $@"{DemoRootD}\demo-samples", 4L * GiB, 1_200, 60, TimeSpan.FromMilliseconds(55)),
        };

    internal static IReadOnlyList<StorageAnalysisItem> GetDemoTopLevelItemsForDrive(string? driveName)
    {
        if (!string.IsNullOrWhiteSpace(driveName) &&
            driveName.Trim().StartsWith("D:", StringComparison.OrdinalIgnoreCase))
        {
            return GetDemoTopLevelItemsForDriveD();
        }

        return GetDemoTopLevelItems();
    }

    internal static string GetDemoNtfsEnumUsnText() =>
        string.Join(
            Environment.NewLine,
            "상태: Completed",
            @"루트: C:\",
            @"볼륨 장치: \\.\C:",
            "NTFS로 식별: 예",
            "읽은 레코드 수: " + (1_200_000L).ToString("N0"),
            "파싱 성공(고유 레코드): " + (1_199_800L).ToString("N0"),
            "소요: 4.800초",
            string.Empty,
            "※ 데모용 고정 문자열입니다. 실제 IOCTL을 실행하지 않았습니다.");

    internal static string GetDemoNtfsTreeText() =>
        string.Join(
            Environment.NewLine,
            "상태: Completed",
            @"루트: C:\",
            @"볼륨 장치: \\.\C:",
            "NTFS로 식별: 예",
            "소요: 4.800초",
            "TotalRecords(원시 슬롯): " + (1_200_000L).ToString("N0"),
            "ParsedRecords(고유 FRN): " + (1_199_800L).ToString("N0"),
            "FileRecords: " + (820_000L).ToString("N0"),
            "DirectoryRecords: " + (310_000L).ToString("N0"),
            "ReparsePointRecords: " + (12_400L).ToString("N0"),
            "UnsupportedVersionRecords: 0",
            "InvalidRecords: 0",
            "LinkedRecords: " + (1_198_000L).ToString("N0"),
            "OrphanRecords: 42",
            "RootCandidateRecords: 3",
            string.Empty,
            "파일 크기/폴더 용량은 아직 계산하지 않습니다.",
            string.Empty,
            "※ 데모용 고정 문자열입니다. 실제 트리 진단을 실행하지 않았습니다.");

    internal static string GetDemoNtfsFileSizeText() =>
        string.Join(
            Environment.NewLine,
            "상태: Completed",
            @"루트: C:\",
            @"볼륨 장치: \\.\C:",
            "NTFS로 식별: 예",
            "소요: 0.120초",
            "요청 샘플 수: 1,000",
            "시도(열기) 수: 1,000",
            "성공: 997",
            "AccessDenied: 0",
            "NotFound: 1",
            "기타 실패: 2",
            "성공률: 99.70%",
            "AccessDenied 비율: 0.00%",
            "실패률(NotFound+기타): 0.30%",
            "샘플 합계 크기: 2.4 GB",
            "처리 속도: 8333.33 files/s",
            string.Empty,
            "직전 트리 진단 FileRecords 820,000개를 모두 이 속도로 조회한다고 가정하면 약 1분 39초가 걸릴 수 있습니다(추정치, 실제 전체 집계 아님).",
            string.Empty,
            "USN 열거 순서에 따른 편향이 남을 수 있습니다. 5,000건 이상은 stride로 구간을 넓힙니다.",
            "전체 용량 계산은 아직 수행하지 않습니다. OpenFileById 샘플 진단만 수행했습니다.",
            string.Empty,
            "샘플(일부):",
            "  • demo-note.txt | 4 KB",
            "  • sample.bin | 12 MB",
            string.Empty,
            "※ 데모용 고정 문자열입니다. 실제 OpenFileById 진단을 실행하지 않았습니다.");

    internal static long GetDemoTreeFileRecordsForEstimate() => 820_000L;

    private static StorageAnalysisItem NewDemoDir(
        string name,
        string path,
        long sizeBytes,
        int fileCount,
        int directoryCount,
        TimeSpan duration) =>
        new(
            name,
            path,
            sizeBytes,
            StorageEntryType.Directory,
            fileCount,
            directoryCount,
            isAccessible: true,
            note: "데모 데이터(실측 아님) — 자동 정리·삭제 대상 아님",
            duration);
}
