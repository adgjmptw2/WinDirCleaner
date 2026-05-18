using WinDirCleaner.Core.Services;

namespace WinDirCleaner.Core.Tests;

public class ReadOnlyDirectorySizeServiceTests
{
    private readonly ReadOnlyDirectorySizeService _sut = new();

    [Fact]
    public async Task CalculateSizeAsync_SumsFilesInDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "wdc-size-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), new string('x', 100));
            await File.WriteAllTextAsync(Path.Combine(root, "b.txt"), new string('y', 50));

            var size = await _sut.CalculateSizeAsync(root);

            Assert.Equal(150L, size);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task CalculateSizeAsync_EmptyDirectory_ReturnsZero()
    {
        var root = Path.Combine(Path.GetTempPath(), "wdc-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var size = await _sut.CalculateSizeAsync(root);
            Assert.Equal(0L, size);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task CalculateSizeAsync_MissingPath_ReturnsZero()
    {
        var missing = Path.Combine(Path.GetTempPath(), "wdc-missing-" + Guid.NewGuid().ToString("N") + "-nope");
        var size = await _sut.CalculateSizeAsync(missing);
        Assert.Equal(0L, size);
    }

    [Fact]
    public async Task CalculateSizeAsync_PreCanceledToken_ThrowsOperationCanceledException()
    {
        var root = Path.Combine(Path.GetTempPath(), "wdc-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    async () => await _sut.CalculateSizeAsync(root, cts.Token));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
