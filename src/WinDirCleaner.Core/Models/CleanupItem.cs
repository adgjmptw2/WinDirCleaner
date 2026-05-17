namespace WinDirCleaner.Core.Models;

public sealed class CleanupItem
{
    public CleanupItem(
        string id,
        string name,
        string path,
        long sizeBytes,
        CleanupRisk risk,
        bool selected,
        string description,
        string reason,
        string impact)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id is required.", nameof(id));
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "SizeBytes cannot be negative.");
        }

        Id = id;
        Name = name;
        Path = path;
        SizeBytes = sizeBytes;
        Risk = risk;
        Description = description;
        Reason = reason;
        Impact = impact;

        if (risk == CleanupRisk.Dangerous)
        {
            Selectable = false;
            CanDelete = false;
            Selected = false;
        }
        else
        {
            Selectable = true;
            CanDelete = true;
            Selected = selected;
        }
    }

    public string Id { get; }

    public string Name { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public CleanupRisk Risk { get; }

    public bool Selectable { get; }

    public bool Selected { get; }

    public string Description { get; }

    public string Reason { get; }

    public string Impact { get; }

    public bool CanDelete { get; }
}
