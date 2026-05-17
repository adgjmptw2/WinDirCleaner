using System.IO;
using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Services;

namespace WinDirCleaner.Core.Tests;

public class DriveInfoServiceTests
{
    [Fact]
    public void GetReadyDrives_DoesNotThrow_AndReturnsNonNull()
    {
        var service = new DriveInfoService();

        var drives = service.GetReadyDrives();

        Assert.NotNull(drives);
    }

    [Fact]
    public void GetReadyDrives_EachEntry_HasValidMetrics()
    {
        var service = new DriveInfoService();
        var drives = service.GetReadyDrives();

        foreach (var d in drives)
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.True(d.TotalBytes >= 0);
            Assert.True(d.UsedBytes >= 0);
            Assert.True(d.FreeBytes >= 0);
            Assert.True(d.UsedBytes <= d.TotalBytes);
            Assert.True(d.FreeBytes <= d.TotalBytes);
        }
    }

    [Fact]
    public void GetOrderedDriveBasicInfos_DoesNotThrow_AndReturnsNonNull()
    {
        var service = new DriveInfoService();

        var list = service.GetOrderedDriveBasicInfos();

        Assert.NotNull(list);
    }

    [Fact]
    public void GetOrderedDriveBasicInfos_EachEntry_HasNameAndExpectedInitialState()
    {
        var service = new DriveInfoService();
        var list = service.GetOrderedDriveBasicInfos();

        foreach (var b in list)
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Name));
            Assert.True(b.InitialCapacityStatus is DriveLoadStatus.Loading or DriveLoadStatus.Skipped);
        }
    }

    [Fact]
    public void DriveBasicInfo_Constructor_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() =>
            new DriveBasicInfo(" ", DriveType.Fixed, DriveLoadStatus.Loading, "x"));
    }

    [Fact]
    public void DriveLoadStatus_HasSixValues()
    {
        var values = Enum.GetValues<DriveLoadStatus>();
        Assert.Equal(6, values.Length);
    }

    [Theory]
    [InlineData(DriveType.Fixed, 0)]
    [InlineData(DriveType.Removable, 1)]
    [InlineData(DriveType.Network, 10)]
    public void GetDriveTypeSortKey_MatchesContract(DriveType type, int expected)
    {
        Assert.Equal(expected, DriveInfoService.GetDriveTypeSortKey(type));
    }

    [Fact]
    public async Task ProbeDriveCapacityAsync_Network_ReturnsSkippedWithoutDelay()
    {
        var service = new DriveInfoService();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var r = await service.ProbeDriveCapacityAsync(@"Z:\", DriveType.Network, TimeSpan.FromSeconds(2));

        sw.Stop();
        Assert.Equal(DriveLoadStatus.Skipped, r.Status);
        Assert.Null(r.Summary);
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
        Assert.True(sw.ElapsedMilliseconds < 500, "Skipped probe should not wait for I/O.");
    }

    [Fact]
    public void DriveCapacityProbeResult_AllowsEmptyMessage()
    {
        var r = new DriveCapacityProbeResult(DriveLoadStatus.Ready, null, string.Empty);
        Assert.Equal(string.Empty, r.Message);
    }
}
