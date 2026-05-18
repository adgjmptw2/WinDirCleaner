using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Services;

namespace WinDirCleaner.Core.Tests;

public class NtfsPathMappingProbeServiceTests
{
    private readonly NtfsPathMappingProbeService _sut = new();

    [Fact]
    public void NtfsPathMappingProbeResult_NegativeCounts_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NtfsPathMappingProbeResult(
                NtfsPathMappingStatus.Completed,
                "a",
                "b",
                "c",
                true,
                recordsScanned: -1,
                parsedRecords: 0,
                TimeSpan.Zero));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NtfsPathMappingProbeResult(
                NtfsPathMappingStatus.Completed,
                "a",
                "b",
                "c",
                true,
                recordsScanned: 0,
                parsedRecords: -1,
                TimeSpan.Zero));
    }

    [Fact]
    public async Task ProbePathMappingAsync_EmptyPath_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () => await _sut.ProbePathMappingAsync("   "));
    }

    [Fact]
    public async Task ProbePathMappingAsync_NonexistentPath_ReturnsPathNotFoundWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), "wdc-ntfs-map-missing-" + Guid.NewGuid().ToString("N") + "\\nope");
        var r = await _sut.ProbePathMappingAsync(path);
        Assert.Equal(NtfsPathMappingStatus.PathNotFound, r.Status);
    }

    [Fact]
    public void TryMatchParentChainToVolumeRoot_MatchingChain_ReturnsTrue()
    {
        const string rootFrn = "0005000000000005";
        var work = new NtfsFileRecord(
            "0000000000000101",
            rootFrn,
            "Work",
            NtfsUsnRecordKind.Directory,
            0x10,
            2,
            0);

        var data = new NtfsFileRecord(
            "0000000000000202",
            work.FileReferenceNumber,
            "data.txt",
            NtfsUsnRecordKind.File,
            0,
            2,
            0);

        var dict = new Dictionary<string, NtfsFileRecord>(StringComparer.Ordinal)
        {
            [work.FileReferenceNumber] = work,
            [data.FileReferenceNumber] = data,
        };

        var segments = new[] { "Work", "data.txt" };

        Assert.True(NtfsPathMappingProbeService.TryMatchParentChainToVolumeRoot(data, segments, dict));
    }

    [Fact]
    public void TryMatchParentChainToVolumeRoot_WrongParentName_ReturnsFalse()
    {
        const string rootFrn = "0005000000000005";
        var wrong = new NtfsFileRecord(
            "0000000000000101",
            rootFrn,
            "Other",
            NtfsUsnRecordKind.Directory,
            0x10,
            2,
            0);

        var data = new NtfsFileRecord(
            "0000000000000202",
            wrong.FileReferenceNumber,
            "data.txt",
            NtfsUsnRecordKind.File,
            0,
            2,
            0);

        var dict = new Dictionary<string, NtfsFileRecord>(StringComparer.Ordinal)
        {
            [wrong.FileReferenceNumber] = wrong,
            [data.FileReferenceNumber] = data,
        };

        var segments = new[] { "Work", "data.txt" };

        Assert.False(NtfsPathMappingProbeService.TryMatchParentChainToVolumeRoot(data, segments, dict));
    }

    [Fact]
    public async Task ProbePathMappingAsync_NonWindows_ReturnsUnsupported()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var r = await _sut.ProbePathMappingAsync(@"C:\");
        Assert.Equal(NtfsPathMappingStatus.Unsupported, r.Status);
    }
}
