namespace WinDirCleaner.Core.Models;

public sealed class NtfsFileSizeProbeSample
{
    public NtfsFileSizeProbeSample(
        string fileReferenceNumber,
        string name,
        bool success,
        long? sizeBytes,
        string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(fileReferenceNumber))
        {
            throw new ArgumentException("File reference number is required.", nameof(fileReferenceNumber));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (sizeBytes.HasValue && sizeBytes.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes.Value, "Size cannot be negative.");
        }

        FileReferenceNumber = fileReferenceNumber.Trim();
        Name = name;
        Success = success;
        SizeBytes = sizeBytes;
        ErrorMessage = errorMessage;
    }

    public string FileReferenceNumber { get; }

    public string Name { get; }

    public bool Success { get; }

    public long? SizeBytes { get; }

    public string? ErrorMessage { get; }
}
