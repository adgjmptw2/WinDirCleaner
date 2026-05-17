namespace WinDirCleaner.Core.Models;

public sealed class NtfsFileRecord
{
    public NtfsFileRecord(
        string fileReferenceNumber,
        string parentFileReferenceNumber,
        string name,
        NtfsUsnRecordKind kind,
        uint fileAttributes,
        ushort majorVersion,
        ushort minorVersion)
    {
        if (string.IsNullOrWhiteSpace(fileReferenceNumber))
        {
            throw new ArgumentException("File reference number is required.", nameof(fileReferenceNumber));
        }

        if (string.IsNullOrWhiteSpace(parentFileReferenceNumber))
        {
            throw new ArgumentException("Parent file reference number is required.", nameof(parentFileReferenceNumber));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        FileReferenceNumber = fileReferenceNumber.Trim();
        ParentFileReferenceNumber = parentFileReferenceNumber.Trim();
        Name = name;
        Kind = kind;
        FileAttributes = fileAttributes;
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
    }

    public string FileReferenceNumber { get; }

    public string ParentFileReferenceNumber { get; }

    public string Name { get; }

    public NtfsUsnRecordKind Kind { get; }

    public uint FileAttributes { get; }

    public ushort MajorVersion { get; }

    public ushort MinorVersion { get; }

    public bool IsDirectory => Kind == NtfsUsnRecordKind.Directory;

    public bool IsReparsePoint => Kind == NtfsUsnRecordKind.ReparsePoint;
}
