namespace WinDirCleaner.Core.Models;

public sealed class DriveCapacityProbeResult
{
    public DriveCapacityProbeResult(DriveLoadStatus status, DriveSummary? summary, string message)
    {
        Status = status;
        Summary = summary;
        Message = message ?? string.Empty;
    }

    public DriveLoadStatus Status { get; }

    public DriveSummary? Summary { get; }

    public string Message { get; }
}
