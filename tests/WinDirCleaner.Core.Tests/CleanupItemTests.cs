using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Tests;

public class CleanupItemTests
{
    [Fact]
    public void DangerousItem_IsNotSelectable()
    {
        var item = Create(CleanupRisk.Dangerous);

        Assert.False(item.Selectable);
    }

    [Fact]
    public void DangerousItem_CannotDelete()
    {
        var item = Create(CleanupRisk.Dangerous);

        Assert.False(item.CanDelete);
    }

    [Fact]
    public void RecommendedItem_IsSelectableByDefault()
    {
        var item = Create(CleanupRisk.Recommended);

        Assert.True(item.Selectable);
    }

    [Fact]
    public void NegativeSizeBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CleanupItem(
            id: "x",
            name: "x",
            path: "C:\\",
            sizeBytes: -1,
            risk: CleanupRisk.Recommended,
            selected: false,
            description: "",
            reason: "",
            impact: ""));
    }

    private static CleanupItem Create(CleanupRisk risk) =>
        new(
            id: "test",
            name: "Test",
            path: "C:\\Temp",
            sizeBytes: 0,
            risk: risk,
            selected: true,
            description: "",
            reason: "",
            impact: "");
}
