namespace WinDirCleaner.Core.Models;

public sealed class CleanupPreviewTarget
{
    public CleanupPreviewTarget(
        string name,
        string path,
        long sizeBytes,
        bool isDirectory,
        string sourceCandidateId,
        string sourceCandidateName,
        string? note = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(sourceCandidateId))
        {
            throw new ArgumentException("SourceCandidateId is required.", nameof(sourceCandidateId));
        }

        if (string.IsNullOrWhiteSpace(sourceCandidateName))
        {
            throw new ArgumentException("SourceCandidateName is required.", nameof(sourceCandidateName));
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "SizeBytes cannot be negative.");
        }

        Name = name;
        Path = path;
        SizeBytes = sizeBytes;
        IsDirectory = isDirectory;
        SourceCandidateId = sourceCandidateId;
        SourceCandidateName = sourceCandidateName;
        Note = note;
    }

    public string Name { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public bool IsDirectory { get; }

    public string SourceCandidateId { get; }

    public string SourceCandidateName { get; }

    public string? Note { get; }
}
