using System.IO;
using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

public sealed class DriveInfoService : IDriveInfoService
{
    private const string DefaultVolumeLabel = "로컬 디스크";

    public IReadOnlyList<DriveSummary> GetReadyDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            return Array.Empty<DriveSummary>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<DriveSummary>();
        }

        var result = new List<DriveSummary>();

        foreach (var drive in drives)
        {
            if (!TryBuildSummary(drive, out var summary))
            {
                continue;
            }

            result.Add(summary);
        }

        result.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    public IReadOnlyList<DriveBasicInfo> GetOrderedDriveBasicInfos()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            return Array.Empty<DriveBasicInfo>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<DriveBasicInfo>();
        }

        var list = new List<DriveBasicInfo>();

        foreach (var drive in drives)
        {
            string name;
            try
            {
                name = drive.Name;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            DriveType driveType;
            try
            {
                driveType = drive.DriveType;
            }
            catch (IOException)
            {
                driveType = DriveType.Unknown;
            }
            catch (UnauthorizedAccessException)
            {
                driveType = DriveType.Unknown;
            }

            var (initialStatus, message) = GetInitialCapacityPresentation(driveType);
            list.Add(new DriveBasicInfo(name, driveType, initialStatus, message));
        }

        list.Sort(CompareDriveBasicInfos);
        return list;
    }

    public async Task<DriveCapacityProbeResult> ProbeDriveCapacityAsync(
        string rootName,
        DriveType driveType,
        TimeSpan capacityQueryBudget)
    {
        if (ShouldSkipCapacityProbe(driveType))
        {
            return new DriveCapacityProbeResult(
                DriveLoadStatus.Skipped,
                null,
                GetSkippedCapacityMessage(driveType));
        }

        if (capacityQueryBudget <= TimeSpan.Zero)
        {
            return new DriveCapacityProbeResult(
                DriveLoadStatus.Timeout,
                null,
                "조회 지연: 응답이 지연되어 용량을 표시하지 않습니다. 필요하면 새로고침해 보세요.");
        }

        var probeTask = Task.Run(() => ProbeCapacityCore(rootName));
        var winner = await Task.WhenAny(probeTask, Task.Delay(capacityQueryBudget)).ConfigureAwait(false);

        if (winner != probeTask)
        {
            return new DriveCapacityProbeResult(
                DriveLoadStatus.Timeout,
                null,
                "조회 지연: 응답이 지연되어 용량을 표시하지 않습니다. 필요하면 새로고침해 보세요.");
        }

        return await probeTask.ConfigureAwait(false);
    }

    private static (DriveLoadStatus Status, string Message) GetInitialCapacityPresentation(DriveType driveType)
    {
        if (ShouldSkipCapacityProbe(driveType))
        {
            return (DriveLoadStatus.Skipped, GetSkippedCapacityMessage(driveType));
        }

        return (DriveLoadStatus.Loading, "용량 확인 중…");
    }

    private static bool ShouldSkipCapacityProbe(DriveType driveType) =>
        driveType is DriveType.Network
        or DriveType.CDRom
        or DriveType.Ram
        or DriveType.Unknown
        or DriveType.NoRootDirectory;

    private static string GetSkippedCapacityMessage(DriveType driveType) =>
        driveType switch
        {
            DriveType.Network => "네트워크 드라이브는 기본적으로 용량 조회를 생략합니다.",
            DriveType.CDRom => "CD/DVD 드라이브는 용량 조회를 생략합니다.",
            DriveType.Ram => "RAM 디스크는 용량 조회를 생략합니다.",
            DriveType.NoRootDirectory => "루트 경로가 없어 용량 조회를 생략합니다.",
            _ => "이 드라이브 유형은 용량 조회를 생략합니다.",
        };

    private static int CompareDriveBasicInfos(DriveBasicInfo a, DriveBasicInfo b)
    {
        var typeCmp = GetDriveTypeSortKey(a.DriveType).CompareTo(GetDriveTypeSortKey(b.DriveType));
        if (typeCmp != 0)
        {
            return typeCmp;
        }

        return string.CompareOrdinal(a.Name, b.Name);
    }
    public static int GetDriveTypeSortKey(DriveType driveType) =>
        driveType switch
        {
            DriveType.Fixed => 0,
            DriveType.Removable => 1,
            DriveType.Network => 10,
            DriveType.CDRom => 11,
            DriveType.Ram => 12,
            DriveType.NoRootDirectory => 20,
            _ => 19,
        };

    private static DriveCapacityProbeResult ProbeCapacityCore(string rootName)
    {
        DriveInfo drive;
        try
        {
            drive = new DriveInfo(rootName);
        }
        catch (IOException)
        {
            return new DriveCapacityProbeResult(
                DriveLoadStatus.Failed,
                null,
                "드라이브 정보를 열 수 없습니다.");
        }
        catch (UnauthorizedAccessException)
        {
            return new DriveCapacityProbeResult(
                DriveLoadStatus.Failed,
                null,
                "드라이브 정보에 접근할 수 없습니다.");
        }

        try
        {
            if (!drive.IsReady)
            {
                return new DriveCapacityProbeResult(
                    DriveLoadStatus.NotReady,
                    null,
                    "드라이브가 준비되지 않았습니다.");
            }
        }
        catch (IOException)
        {
            return new DriveCapacityProbeResult(
                DriveLoadStatus.Failed,
                null,
                "준비 상태를 확인할 수 없습니다.");
        }
        catch (UnauthorizedAccessException)
        {
            return new DriveCapacityProbeResult(
                DriveLoadStatus.Failed,
                null,
                "준비 상태를 확인할 수 없습니다.");
        }

        if (!TryBuildSummary(drive, out var summary))
        {
            return new DriveCapacityProbeResult(
                DriveLoadStatus.Failed,
                null,
                "용량 정보를 읽을 수 없습니다.");
        }

        return new DriveCapacityProbeResult(DriveLoadStatus.Ready, summary, string.Empty);
    }

    private static bool TryBuildSummary(DriveInfo drive, out DriveSummary summary)
    {
        summary = default!;

        try
        {
            if (!drive.IsReady)
            {
                return false;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        string name;
        try
        {
            name = drive.Name;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        long totalBytes;
        long freeBytes;
        try
        {
            totalBytes = drive.TotalSize;
            freeBytes = drive.AvailableFreeSpace;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (totalBytes < 0 || freeBytes < 0)
        {
            return false;
        }

        if (freeBytes > totalBytes)
        {
            freeBytes = totalBytes;
        }

        var usedBytes = totalBytes - freeBytes;
        if (usedBytes < 0)
        {
            usedBytes = 0;
        }

        var label = TryGetVolumeLabel(drive);

        try
        {
            summary = new DriveSummary(name, label, totalBytes, usedBytes, freeBytes);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string TryGetVolumeLabel(DriveInfo drive)
    {
        string? raw = null;
        try
        {
            raw = drive.VolumeLabel;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultVolumeLabel;
        }

        return raw.Trim();
    }
}
