namespace WinDirCleaner.Core.Models;

public sealed class DriveBasicInfo
{
    public DriveBasicInfo(
        string name,
        DriveType driveType,
        DriveLoadStatus initialCapacityStatus,
        string initialStatusMessage)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        Name = name;
        DriveType = driveType;
        InitialCapacityStatus = initialCapacityStatus;
        InitialStatusMessage = initialStatusMessage ?? string.Empty;
    }

    public string Name { get; }

    public DriveType DriveType { get; }

    public DriveLoadStatus InitialCapacityStatus { get; }

    public string InitialStatusMessage { get; }
}
