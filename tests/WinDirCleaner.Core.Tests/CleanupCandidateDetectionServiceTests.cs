using System.Linq;
using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Services;

namespace WinDirCleaner.Core.Tests;

public class CleanupCandidateDetectionServiceTests
{
    private sealed class RecordingSizeService : IReadOnlyDirectorySizeService
    {
        public List<string> CalledPaths { get; } = new();

        public Task<long> CalculateSizeAsync(string path, CancellationToken cancellationToken = default)
        {
            CalledPaths.Add(path);
            return Task.FromResult(42L);
        }
    }

    [Fact]
    public async Task DetectCandidatesAsync_IncludesAllRiskLevels()
    {
        var fake = new RecordingSizeService();
        var sut = new CleanupCandidateDetectionService(fake);

        var items = await sut.DetectCandidatesAsync();

        Assert.Contains(items, x => x.Risk == CleanupRisk.Recommended);
        Assert.Contains(items, x => x.Risk == CleanupRisk.Optional);
        Assert.Contains(items, x => x.Risk == CleanupRisk.Dangerous);
    }

    [Fact]
    public async Task DetectCandidatesAsync_DangerousItems_AreLockedAndNotSelected()
    {
        var sut = new CleanupCandidateDetectionService(new RecordingSizeService());
        var items = await sut.DetectCandidatesAsync();

        foreach (var x in items.Where(x => x.Risk == CleanupRisk.Dangerous))
        {
            Assert.False(x.Selectable);
            Assert.False(x.CanDelete);
            Assert.False(x.Selected);
        }
    }

    [Fact]
    public async Task DetectCandidatesAsync_DoesNotCallSizeServiceForDangerousPaths()
    {
        var fake = new RecordingSizeService();
        var sut = new CleanupCandidateDetectionService(fake);

        _ = await sut.DetectCandidatesAsync();

        Assert.DoesNotContain(fake.CalledPaths, p => p.Contains("WinSxS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fake.CalledPaths, p => p.Contains("DriverStore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DetectCandidatesAsync_CallsSizeServiceForFourSafeTargets()
    {
        var fake = new RecordingSizeService();
        var sut = new CleanupCandidateDetectionService(fake);

        _ = await sut.DetectCandidatesAsync();

        Assert.Equal(4, fake.CalledPaths.Count);
    }

    [Fact]
    public async Task DetectCandidatesAsync_AllItemsHaveNonEmptyNarrativeFields()
    {
        var sut = new CleanupCandidateDetectionService(new RecordingSizeService());
        var items = await sut.DetectCandidatesAsync();

        foreach (var x in items)
        {
            Assert.False(string.IsNullOrWhiteSpace(x.Description));
            Assert.False(string.IsNullOrWhiteSpace(x.Reason));
            Assert.False(string.IsNullOrWhiteSpace(x.Impact));
        }
    }
}
