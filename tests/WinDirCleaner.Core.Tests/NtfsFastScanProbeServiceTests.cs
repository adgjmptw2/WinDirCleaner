using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Services;

namespace WinDirCleaner.Core.Tests;

public sealed class NtfsFastScanProbeServiceTests
{
    [Fact]
    public void NtfsFastScanProbeResult_RejectsNegativeRecordsRead()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NtfsFastScanProbeResult(
                NtfsFastScanStatus.Completed,
                @"C:\",
                @"\\.\C:",
                isNtfs: true,
                recordsRead: -1,
                TimeSpan.Zero));
    }

    [Fact]
    public void NtfsFastScanProbeResult_RejectsEmptyRootPath()
    {
        Assert.Throws<ArgumentException>(() =>
            new NtfsFastScanProbeResult(
                NtfsFastScanStatus.Failed,
                " ",
                @"\\.\C:",
                isNtfs: false,
                recordsRead: 0,
                TimeSpan.Zero));
    }

    [Fact]
    public void NtfsFastScanProbeResult_RejectsEmptyVolumePath()
    {
        Assert.Throws<ArgumentException>(() =>
            new NtfsFastScanProbeResult(
                NtfsFastScanStatus.Failed,
                @"C:\",
                " ",
                isNtfs: false,
                recordsRead: 0,
                TimeSpan.Zero));
    }

    [Fact]
    public async Task ProbeAsync_EmptyRootPath_ThrowsArgumentException()
    {
        var svc = new NtfsFastScanProbeService();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ProbeAsync("   "));
    }

    [Fact]
    public async Task ProbeAsync_NonWindows_ReturnsApiUnavailable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var svc = new NtfsFastScanProbeService();
        var r = await svc.ProbeAsync(@"C:\");
        Assert.Equal(NtfsFastScanStatus.ApiUnavailable, r.Status);
        Assert.Equal(0, r.RecordsRead);
    }

    [Fact]
    public async Task ProbeAsync_TempDriveRoot_ReturnsResultWithoutThrowing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.GetPathRoot(Path.GetTempPath());
        Assert.False(string.IsNullOrEmpty(root));

        var svc = new NtfsFastScanProbeService();
        var r = await svc.ProbeAsync(root!);

        Assert.True(r.RecordsRead >= 0);
        Assert.False(string.IsNullOrWhiteSpace(r.RootPath));
        Assert.False(string.IsNullOrWhiteSpace(r.VolumePath));
        Assert.True(
            r.Status is NtfsFastScanStatus.Completed
                or NtfsFastScanStatus.NotNtfs
                or NtfsFastScanStatus.AccessDenied
                or NtfsFastScanStatus.ApiUnavailable
                or NtfsFastScanStatus.Failed);
    }
}
