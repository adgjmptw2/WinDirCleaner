namespace WinDirCleaner.Core.Models;

public sealed class DriveSummary
{
    public DriveSummary(
        string name,
        string label,
        long totalBytes,
        long usedBytes,
        long freeBytes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (totalBytes < 0 || usedBytes < 0 || freeBytes < 0)
        {
            throw new ArgumentOutOfRangeException("Byte counts cannot be negative.");
        }

        Name = name;
        Label = label;
        TotalBytes = totalBytes;
        UsedBytes = usedBytes;
        FreeBytes = freeBytes;
    }

    public string Name { get; }

    public string Label { get; }

    public long TotalBytes { get; }

    public long UsedBytes { get; }

    public long FreeBytes { get; }

    public double UsedPercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0.0;
}
