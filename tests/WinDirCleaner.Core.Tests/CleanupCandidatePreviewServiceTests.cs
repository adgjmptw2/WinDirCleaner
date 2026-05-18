using System.Linq;
using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Services;

namespace WinDirCleaner.Core.Tests;

public class CleanupCandidatePreviewServiceTests
{
    private readonly CleanupCandidatePreviewService _sut = new();

    [Fact]
    public void GetPreviewCandidates_ReturnsNonEmptyList()
    {
        var list = _sut.GetPreviewCandidates();

        Assert.NotEmpty(list);
    }

    [Fact]
    public void GetPreviewCandidates_IncludesRecommended()
    {
        Assert.Contains(_sut.GetPreviewCandidates(), x => x.Risk == CleanupRisk.Recommended);
    }

    [Fact]
    public void GetPreviewCandidates_IncludesOptional()
    {
        Assert.Contains(_sut.GetPreviewCandidates(), x => x.Risk == CleanupRisk.Optional);
    }

    [Fact]
    public void GetPreviewCandidates_IncludesDangerous()
    {
        Assert.Contains(_sut.GetPreviewCandidates(), x => x.Risk == CleanupRisk.Dangerous);
    }

    [Fact]
    public void DangerousItems_AreNotSelectableAndCannotDelete_AndNotSelected()
    {
        foreach (var x in _sut.GetPreviewCandidates().Where(x => x.Risk == CleanupRisk.Dangerous))
        {
            Assert.False(x.Selectable);
            Assert.False(x.CanDelete);
            Assert.False(x.Selected);
        }
    }

    [Fact]
    public void AllItems_HaveNonEmptyCoreTextFields()
    {
        foreach (var x in _sut.GetPreviewCandidates())
        {
            Assert.False(string.IsNullOrWhiteSpace(x.Id));
            Assert.False(string.IsNullOrWhiteSpace(x.Name));
            Assert.False(string.IsNullOrWhiteSpace(x.Description));
            Assert.False(string.IsNullOrWhiteSpace(x.Reason));
            Assert.False(string.IsNullOrWhiteSpace(x.Impact));
        }
    }

    [Fact]
    public async Task DetectCandidatesAsync_ReturnsSameCountAsGetPreview()
    {
        var preview = _sut.GetPreviewCandidates();
        var detect = await _sut.DetectCandidatesAsync();
        Assert.Equal(preview.Count, detect.Count);
    }
}
