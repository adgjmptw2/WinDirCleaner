namespace WinDirCleaner.Core.Models;

public enum NtfsUsnRecordKind
{
    File = 0,

    Directory,

    ReparsePoint,

    Other,
}
