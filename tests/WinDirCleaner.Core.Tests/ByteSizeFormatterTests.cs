using WinDirCleaner.Core.Formatting;

namespace WinDirCleaner.Core.Tests;

public class ByteSizeFormatterTests
{
    [Fact]
    public void ZeroBytes_DisplaysAsZeroB()
    {
        Assert.Equal("0 B", ByteSizeFormatter.Format(0));
    }

    [Fact]
    public void FiveTwelveBytes_DisplaysAsBytes()
    {
        Assert.Equal("512 B", ByteSizeFormatter.Format(512));
    }

    [Fact]
    public void OneKibiByte_DisplaysAsKb()
    {
        Assert.Equal("1.0 KB", ByteSizeFormatter.Format(1024));
    }

    [Fact]
    public void OneGibiByte_DisplaysAsGb()
    {
        const long oneGb = 1024L * 1024L * 1024L;
        Assert.Equal("1.0 GB", ByteSizeFormatter.Format(oneGb));
    }

    [Fact]
    public void NegativeBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteSizeFormatter.Format(-1));
    }
}
