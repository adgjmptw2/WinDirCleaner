using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Services;

namespace WinDirCleaner.Core.Tests;

public sealed class NtfsFileSizeProbeServiceTests
{
    [Fact]
    public void NtfsFileSizeProbeSample_RejectsNegativeSizeBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NtfsFileSizeProbeSample("0000000000000001", "a.txt", true, -1L, null));
    }

    [Fact]
    public void NtfsFileSizeProbeSample_RejectsEmptyFrn()
    {
        Assert.Throws<ArgumentException>(() =>
            new NtfsFileSizeProbeSample(" ", "a.txt", false, null, "x"));
    }

    [Fact]
    public void NtfsFileSizeProbeSummary_Rates_AreBetweenZeroAndOne()
    {
        var s = new NtfsFileSizeProbeSummary(
            requestedSampleCount: 100,
            attemptedCount: 10,
            successCount: 7,
            accessDeniedCount: 1,
            notFoundCount: 1,
            failedCount: 1,
            totalSampledSizeBytes: 0,
            elapsed: TimeSpan.FromSeconds(1),
            filesPerSecond: 10,
            successRate: 0.7,
            accessDeniedRate: 0.1,
            failureRate: 0.2);

        Assert.InRange(s.SuccessRate, 0, 1);
        Assert.InRange(s.AccessDeniedRate, 0, 1);
        Assert.InRange(s.FailureRate, 0, 1);
    }

    [Fact]
    public void NtfsFileSizeProbeSummary_RejectsRateAboveOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NtfsFileSizeProbeSummary(
                1,
                1,
                1,
                0,
                0,
                0,
                0,
                TimeSpan.Zero,
                0,
                successRate: 1.01,
                accessDeniedRate: 0,
                failureRate: 0));
    }

    [Fact]
    public void NtfsFileSizeProbeSummary_RejectsNegativeFilesPerSecond()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NtfsFileSizeProbeSummary(
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                TimeSpan.Zero,
                filesPerSecond: -1,
                successRate: 0,
                accessDeniedRate: 0,
                failureRate: 0));
    }

    [Fact]
    public async Task ProbeFileSizesAsync_EmptyRootPath_ThrowsArgumentException()
    {
        var svc = new NtfsFileSizeProbeService();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ProbeFileSizesAsync("   "));
    }

    [Fact]
    public async Task ProbeFileSizesAsync_OnNonWindows_SampleCountClamped_ToMax50000()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var svc = new NtfsFileSizeProbeService();
        var r = await svc.ProbeFileSizesAsync(@"C:\", 999_999);
        Assert.Equal(NtfsFastScanStatus.ApiUnavailable, r.Status);
        Assert.Equal(NtfsFileSizeProbeService.MaxSampleCount, r.Summary.RequestedSampleCount);
        AssertSummaryNonNegative(r.Summary);
    }

    [Fact]
    public async Task ProbeFileSizesAsync_OnNonWindows_SampleCount50000_Allowed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var svc = new NtfsFileSizeProbeService();
        var r = await svc.ProbeFileSizesAsync(@"C:\", 50_000);
        Assert.Equal(50_000, r.Summary.RequestedSampleCount);
        AssertSummaryNonNegative(r.Summary);
    }

    [Fact]
    public async Task ProbeFileSizesAsync_OnNonWindows_SampleCountZero_ClampedTo1()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var svc = new NtfsFileSizeProbeService();
        var r = await svc.ProbeFileSizesAsync(@"C:\", 0);
        Assert.Equal(1, r.Summary.RequestedSampleCount);
        AssertSummaryNonNegative(r.Summary);
    }

    [Fact]
    public async Task ProbeFileSizesAsync_NonWindows_ReturnsApiUnavailable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var svc = new NtfsFileSizeProbeService();
        var r = await svc.ProbeFileSizesAsync(@"C:\", 10);
        Assert.Equal(NtfsFastScanStatus.ApiUnavailable, r.Status);
        AssertSummaryNonNegative(r.Summary);
    }

    [Fact]
    public async Task ProbeFileSizesAsync_TempDriveRoot_ReturnsWithoutThrowing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.GetPathRoot(Path.GetTempPath());
        var svc = new NtfsFileSizeProbeService();
        var r = await svc.ProbeFileSizesAsync(root!, 5);

        Assert.True(r.Samples.Count <= 40);
        AssertSummaryNonNegative(r.Summary);
        Assert.False(string.IsNullOrWhiteSpace(r.RootPath));
        Assert.True(
            r.Status is NtfsFastScanStatus.Completed
                or NtfsFastScanStatus.NotNtfs
                or NtfsFastScanStatus.AccessDenied
                or NtfsFastScanStatus.ApiUnavailable
                or NtfsFastScanStatus.Failed);
    }

    [Fact]
    public async Task ProbeFileSizesAsync_TempDriveRoot_SampleCountZero_ClampedTo1()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.GetPathRoot(Path.GetTempPath());
        var svc = new NtfsFileSizeProbeService();
        var r = await svc.ProbeFileSizesAsync(root!, 0);
        Assert.Equal(1, r.Summary.RequestedSampleCount);
        AssertSummaryNonNegative(r.Summary);
    }

    private static void AssertSummaryNonNegative(NtfsFileSizeProbeSummary s)
    {
        Assert.True(s.RequestedSampleCount >= 0);
        Assert.True(s.AttemptedCount >= 0);
        Assert.True(s.SuccessCount >= 0);
        Assert.True(s.AccessDeniedCount >= 0);
        Assert.True(s.NotFoundCount >= 0);
        Assert.True(s.FailedCount >= 0);
        Assert.True(s.TotalSampledSizeBytes >= 0);
        Assert.True(s.Elapsed >= TimeSpan.Zero);
        Assert.True(s.FilesPerSecond >= 0);
        Assert.False(double.IsNaN(s.FilesPerSecond));
        Assert.False(double.IsInfinity(s.FilesPerSecond));
        Assert.InRange(s.SuccessRate, 0, 1);
        Assert.InRange(s.AccessDeniedRate, 0, 1);
        Assert.InRange(s.FailureRate, 0, 1);
    }
}
