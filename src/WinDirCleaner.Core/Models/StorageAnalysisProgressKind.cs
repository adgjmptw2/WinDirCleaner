namespace WinDirCleaner.Core.Models;

public enum StorageAnalysisProgressKind
{
    Started,

    TopLevelItemStarted,

    Scanning,

    TopLevelItemCompleted,

    Completed,

    Cancelled,
}
