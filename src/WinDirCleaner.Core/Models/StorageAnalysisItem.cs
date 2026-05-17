namespace WinDirCleaner.Core.Models;

public sealed class StorageAnalysisItem
{
    public StorageAnalysisItem(
        string name,
        string path,
        long sizeBytes,
        StorageEntryType entryType,
        int fileCount,
        int directoryCount,
        bool isAccessible,
        string? note,
        TimeSpan topLevelAnalysisDuration = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "SizeBytes cannot be negative.");
        }

        if (fileCount < 0 || directoryCount < 0)
        {
            throw new ArgumentOutOfRangeException("Counts cannot be negative.");
        }

        Name = name;
        Path = path;
        SizeBytes = sizeBytes;
        EntryType = entryType;
        FileCount = fileCount;
        DirectoryCount = directoryCount;
        IsAccessible = isAccessible;
        Note = note;
        TopLevelAnalysisDuration = topLevelAnalysisDuration;
    }
    public TimeSpan TopLevelAnalysisDuration { get; }

    public string Name { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public StorageEntryType EntryType { get; }

    public int FileCount { get; }

    public int DirectoryCount { get; }

    public bool IsAccessible { get; }

    public string? Note { get; }
}
