using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Services;

namespace WinDirCleaner.Core.Tests;

public class CleanupPreviewServiceTests
{
    private readonly CleanupPreviewService _sut = new();

    [Fact]
    public void IsDryRunEligible_RecommendedSelectedTrue_ReturnsTrue()
    {
        var c = new CleanupItem(
            "a",
            "n",
            @"C:\temp",
            0,
            CleanupRisk.Recommended,
            selected: true,
            "d",
            "r",
            "i");

        Assert.True(CleanupPreviewService.IsDryRunEligible(c));
    }

    [Fact]
    public void IsDryRunEligible_NotSelected_ReturnsFalse()
    {
        var c = new CleanupItem(
            "a",
            "n",
            @"C:\temp",
            0,
            CleanupRisk.Recommended,
            selected: false,
            "d",
            "r",
            "i");

        Assert.False(CleanupPreviewService.IsDryRunEligible(c));
    }

    [Fact]
    public void IsDryRunEligible_Dangerous_ReturnsFalse()
    {
        var c = new CleanupItem(
            "x",
            "WinSxS",
            @"C:\Windows\WinSxS",
            0,
            CleanupRisk.Dangerous,
            selected: false,
            "d",
            "r",
            "i");

        Assert.False(CleanupPreviewService.IsDryRunEligible(c));
    }

    [Fact]
    public async Task PreviewAsync_OnlySelectedRootsAreEnumerated()
    {
        var rootA = Path.Combine(Path.GetTempPath(), "wdc-prev-a-" + Guid.NewGuid().ToString("N"));
        var rootB = Path.Combine(Path.GetTempPath(), "wdc-prev-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootA, "only-a.txt"), new string('z', 30));
            await File.WriteAllTextAsync(Path.Combine(rootB, "only-b.txt"), new string('y', 50));

            var items = new[]
            {
                new CleanupItem(
                    "ra",
                    "A",
                    rootA,
                    0,
                    CleanupRisk.Recommended,
                    selected: true,
                    "d",
                    "r",
                    "i"),
                new CleanupItem(
                    "rb",
                    "B",
                    rootB,
                    0,
                    CleanupRisk.Recommended,
                    selected: false,
                    "d",
                    "r",
                    "i"),
            };

            var result = await _sut.PreviewAsync(items);

            Assert.Equal(1, result.Summary.TargetFileCount);
            Assert.Equal(30L, result.Summary.EstimatedBytes);
            Assert.Equal(1, result.Summary.ScannedCandidateCount);
            Assert.Equal(1, result.Summary.SelectedCandidateCount);
            Assert.Equal(0, result.Summary.SkippedCandidateCount);
        }
        finally
        {
            TryDeleteDir(rootA);
            TryDeleteDir(rootB);
        }
    }

    [Fact]
    public async Task PreviewAsync_DangerousCandidate_IsNotScannedWhenUnselected()
    {
        var root = Path.Combine(Path.GetTempPath(), "wdc-prev-d-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "t.txt"), "abc");

            var items = new[]
            {
                new CleanupItem(
                    "dng",
                    "Danger",
                    root,
                    0,
                    CleanupRisk.Dangerous,
                    selected: false,
                    "d",
                    "r",
                    "i"),
                new CleanupItem(
                    "ok",
                    "Good",
                    root,
                    0,
                    CleanupRisk.Recommended,
                    selected: true,
                    "d",
                    "r",
                    "i"),
            };

            var result = await _sut.PreviewAsync(items);

            Assert.Equal(1, result.Summary.TargetFileCount);
            Assert.Equal(1, result.Summary.ScannedCandidateCount);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task PreviewAsync_MissingPath_DoesNotThrow_AndSkips()
    {
        var missing = Path.Combine(Path.GetTempPath(), "wdc-prev-miss-" + Guid.NewGuid().ToString("N") + "-x");
        var items = new[]
        {
            new CleanupItem(
                "m",
                "Missing",
                missing,
                0,
                CleanupRisk.Recommended,
                selected: true,
                "d",
                "r",
                "i"),
        };

        var result = await _sut.PreviewAsync(items);

        Assert.Equal(0, result.Summary.ScannedCandidateCount);
        Assert.Equal(1, result.Summary.SkippedCandidateCount);
        Assert.Equal(0, result.Summary.TargetFileCount);
    }

    [Fact]
    public async Task PreviewAsync_SampleTargets_CappedAt50()
    {
        var root = Path.Combine(Path.GetTempPath(), "wdc-prev-many-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < 60; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, $"f{i}.txt"), "x");
            }

            var items = new[]
            {
                new CleanupItem(
                    "many",
                    "Many",
                    root,
                    0,
                    CleanupRisk.Recommended,
                    selected: true,
                    "d",
                    "r",
                    "i"),
            };

            var result = await _sut.PreviewAsync(items);

            Assert.Equal(60, result.Summary.TargetFileCount);
            Assert.True(result.SampleTargets.Count <= CleanupPreviewService.MaxSampleTargets);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task PreviewAsync_DoesNotRemoveFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "wdc-prev-keep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "keep.txt");
        try
        {
            await File.WriteAllTextAsync(file, "keep-me");

            var items = new[]
            {
                new CleanupItem(
                    "k",
                    "Keep",
                    root,
                    0,
                    CleanupRisk.Recommended,
                    selected: true,
                    "d",
                    "r",
                    "i"),
            };

            await _sut.PreviewAsync(items);

            Assert.True(File.Exists(file));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task PreviewAsync_PreCanceledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var root = Path.Combine(Path.GetTempPath(), "wdc-prev-can-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var items = new[]
            {
                new CleanupItem(
                    "c",
                    "C",
                    root,
                    0,
                    CleanupRisk.Recommended,
                    selected: true,
                    "d",
                    "r",
                    "i"),
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await _sut.PreviewAsync(items, cts.Token));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
