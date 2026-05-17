using System.IO;
using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

public interface IDriveInfoService
{
    IReadOnlyList<DriveSummary> GetReadyDrives();
    IReadOnlyList<DriveBasicInfo> GetOrderedDriveBasicInfos();
    Task<DriveCapacityProbeResult> ProbeDriveCapacityAsync(
        string rootName,
        DriveType driveType,
        TimeSpan capacityQueryBudget);
}
