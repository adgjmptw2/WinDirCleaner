using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Services;

namespace WinDirCleaner.Core.Tests;

public sealed class NtfsFastScanTreeProbeServiceTests
{
    [Fact]
    public async Task ProbeTreeAsync_EmptyRootPath_ThrowsArgumentException()
    {
        var svc = new NtfsFastScanTreeProbeService();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ProbeTreeAsync("   "));
    }

    [Fact]
    public async Task ProbeTreeAsync_NonWindows_ReturnsApiUnavailable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var svc = new NtfsFastScanTreeProbeService();
        var r = await svc.ProbeTreeAsync(@"C:\");
        Assert.Equal(NtfsFastScanStatus.ApiUnavailable, r.Status);
        AssertSummaryNonNegative(r.Summary);
    }

    [Fact]
    public async Task ProbeTreeAsync_TempDriveRoot_ReturnsResultWithoutThrowing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.GetPathRoot(Path.GetTempPath());
        Assert.False(string.IsNullOrEmpty(root));

        var svc = new NtfsFastScanTreeProbeService();
        var r = await svc.ProbeTreeAsync(root!);

        AssertSummaryNonNegative(r.Summary);
        Assert.False(string.IsNullOrWhiteSpace(r.RootPath));
        Assert.False(string.IsNullOrWhiteSpace(r.VolumePath));
        Assert.True(
            r.Status is NtfsFastScanStatus.Completed
                or NtfsFastScanStatus.NotNtfs
                or NtfsFastScanStatus.AccessDenied
                or NtfsFastScanStatus.ApiUnavailable
                or NtfsFastScanStatus.Failed);
    }

    private static void AssertSummaryNonNegative(NtfsFastScanTreeSummary s)
    {
        Assert.True(s.TotalRecords >= 0);
        Assert.True(s.ParsedRecords >= 0);
        Assert.True(s.FileRecords >= 0);
        Assert.True(s.DirectoryRecords >= 0);
        Assert.True(s.ReparsePointRecords >= 0);
        Assert.True(s.UnsupportedVersionRecords >= 0);
        Assert.True(s.InvalidRecords >= 0);
        Assert.True(s.LinkedRecords >= 0);
        Assert.True(s.OrphanRecords >= 0);
        Assert.True(s.RootCandidateRecords >= 0);
        Assert.True(s.Elapsed >= TimeSpan.Zero);
    }
}
