using System.Globalization;
using System.Text;
using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Native;

internal static class NtfsUsnRecordParser
{
    // FSCTL_ENUM_USN_DATA: first 8 bytes = next MFT start FRN; USN_RECORDs follow. Parsed as USN_RECORD V2 only (major == 2).
    internal const int FrnPrefixBytes = 8;

    private const int MinUsnRecordBytes = 60;

    private const int OffRecordLength = 0;

    private const int OffMajorVersion = 4;

    private const int OffMinorVersion = 6;

    private const int OffFileReferenceNumber = 8;

    private const int OffParentFileReferenceNumber = 16;

    private const int OffFileAttributes = 52;

    private const int OffFileNameLength = 56;

    private const int OffFileNameOffset = 58;

    private const uint FileAttributeDirectory = 0x00000010;

    private const uint FileAttributeReparsePoint = 0x00000400;
    internal static bool IsLikelyVolumeRootParentFrn(string normalizedParentFrnHex16) =>
        string.Equals(normalizedParentFrnHex16, "0005000000000005", StringComparison.OrdinalIgnoreCase)
        || string.Equals(normalizedParentFrnHex16, "0000000000000005", StringComparison.OrdinalIgnoreCase);

    internal static string FormatFrnV2(ulong fileReferenceNumber) =>
        fileReferenceNumber.ToString("X16", CultureInfo.InvariantCulture);
    internal static int CountRawRecordSlots(ReadOnlySpan<byte> buffer, int bytesReturned, int maxSingleRecordBytes)
    {
        if (bytesReturned <= FrnPrefixBytes)
        {
            return 0;
        }

        var data = buffer.Slice(0, bytesReturned).Slice(FrnPrefixBytes);
        var offset = 0;
        var count = 0;

        while (offset + sizeof(uint) <= data.Length)
        {
            var recordLength = ReadUInt32LittleEndian(data, offset);
            if (recordLength == 0 || recordLength < MinUsnRecordBytes || recordLength > maxSingleRecordBytes)
            {
                break;
            }

            if (offset + recordLength > data.Length)
            {
                break;
            }

            count++;
            offset += (int)AlignUp8(recordLength);
        }

        return count;
    }
    internal static void ParseUsnDataBuffer(
        ReadOnlySpan<byte> buffer,
        int bytesReturned,
        NtfsUsnRecordParseCounters counters,
        Dictionary<string, NtfsFileRecord> recordsByFrn,
        List<NtfsFileRecord> sampleRecords,
        List<string> sampleRootNames,
        int maxSampleRecords,
        int maxSampleRootNames)
    {
        if (bytesReturned <= FrnPrefixBytes)
        {
            return;
        }

        var span = buffer.Slice(0, bytesReturned);
        var data = span.Slice(FrnPrefixBytes);
        var offset = 0;

        while (offset + sizeof(uint) <= data.Length)
        {
            counters.TotalRawSlots++;

            var recordLength = ReadUInt32LittleEndian(data, offset);
            if (recordLength == 0 || offset + recordLength > data.Length)
            {
                counters.InvalidRecords++;
                break;
            }

            var record = data.Slice(offset, (int)recordLength);
            offset += (int)AlignUp8(recordLength);

            if (!TryParseSingleRecord(record, counters, out var parsed))
            {
                continue;
            }

            recordsByFrn[parsed.FileReferenceNumber] = parsed;

            if (sampleRecords.Count < maxSampleRecords)
            {
                sampleRecords.Add(parsed);
            }

            if (IsLikelyVolumeRootParentFrn(parsed.ParentFileReferenceNumber) &&
                sampleRootNames.Count < maxSampleRootNames &&
                !sampleRootNames.Contains(parsed.Name, StringComparer.Ordinal))
            {
                sampleRootNames.Add(parsed.Name);
            }
        }
    }

    // Distinct File-kind USN records (dedupe by FRN). distinctFileStride > 1 keeps every Nth new file to spread samples along enumeration order.
    internal static void ParseUsnDataBufferCollectDistinctFileRecords(
        ReadOnlySpan<byte> buffer,
        int bytesReturned,
        NtfsUsnRecordParseCounters counters,
        List<NtfsFileRecord> fileRecords,
        HashSet<string> seenFrn,
        int maxDistinctFileRecords,
        int distinctFileStride,
        ref long volumeDistinctNewFileOrdinal)
    {
        if (bytesReturned <= FrnPrefixBytes || fileRecords.Count >= maxDistinctFileRecords)
        {
            return;
        }

        var span = buffer.Slice(0, bytesReturned);
        var data = span.Slice(FrnPrefixBytes);
        var offset = 0;

        while (offset + sizeof(uint) <= data.Length && fileRecords.Count < maxDistinctFileRecords)
        {
            counters.TotalRawSlots++;

            var recordLength = ReadUInt32LittleEndian(data, offset);
            if (recordLength == 0 || offset + recordLength > data.Length)
            {
                counters.InvalidRecords++;
                break;
            }

            var record = data.Slice(offset, (int)recordLength);
            offset += (int)AlignUp8(recordLength);

            if (!TryParseSingleRecord(record, counters, out var parsed))
            {
                continue;
            }

            if (parsed.Kind != NtfsUsnRecordKind.File)
            {
                continue;
            }

            if (!seenFrn.Add(parsed.FileReferenceNumber))
            {
                continue;
            }

            volumeDistinctNewFileOrdinal++;
            var stride = distinctFileStride < 1 ? 1 : distinctFileStride;
            if (stride > 1 && (volumeDistinctNewFileOrdinal - 1) % stride != 0)
            {
                continue;
            }

            fileRecords.Add(parsed);
        }
    }

    internal static bool TryParseSingleRecordForTests(ReadOnlySpan<byte> record, out NtfsFileRecord? parsed)
    {
        var c = new NtfsUsnRecordParseCounters();
        if (!TryParseSingleRecord(record, c, out var p))
        {
            parsed = null;
            return false;
        }

        parsed = p;
        return true;
    }

    internal static bool TryParseSingleRecord(
        ReadOnlySpan<byte> record,
        NtfsUsnRecordParseCounters counters,
        out NtfsFileRecord parsed)
    {
        parsed = null!;
        try
        {
            if (record.Length < MinUsnRecordBytes)
            {
                counters.InvalidRecords++;
                return false;
            }

            var major = ReadUInt16LittleEndian(record, OffMajorVersion);
            var minor = ReadUInt16LittleEndian(record, OffMinorVersion);

            // Field offsets follow USN_RECORD V2 (major == 2).
            if (major != 2)
            {
                counters.UnsupportedVersionRecords++;
                return false;
            }

            var fileRef = ReadUInt64LittleEndian(record, OffFileReferenceNumber);
            var parentRef = ReadUInt64LittleEndian(record, OffParentFileReferenceNumber);
            var attributes = ReadUInt32LittleEndian(record, OffFileAttributes);
            var nameLengthBytes = ReadUInt16LittleEndian(record, OffFileNameLength);
            var nameOffset = ReadUInt16LittleEndian(record, OffFileNameOffset);

            if (nameOffset > record.Length || nameLengthBytes > record.Length || nameOffset + nameLengthBytes > record.Length)
            {
                counters.InvalidRecords++;
                return false;
            }

            if (nameLengthBytes == 0 || (nameLengthBytes & 1) != 0)
            {
                counters.InvalidRecords++;
                return false;
            }

            var nameBytes = record.Slice(nameOffset, nameLengthBytes);
            string name;
            try
            {
                name = Encoding.Unicode.GetString(nameBytes);
            }
            catch
            {
                counters.InvalidRecords++;
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                counters.InvalidRecords++;
                return false;
            }

            var kind = ClassifyKind(attributes);
            var frn = FormatFrnV2(fileRef);
            var pfrn = FormatFrnV2(parentRef);

            parsed = new NtfsFileRecord(frn, pfrn, name, kind, attributes, major, minor);
            return true;
        }
        catch
        {
            counters.InvalidRecords++;
            return false;
        }
    }

    private static NtfsUsnRecordKind ClassifyKind(uint attributes)
    {
        if ((attributes & FileAttributeReparsePoint) != 0)
        {
            return NtfsUsnRecordKind.ReparsePoint;
        }

        if ((attributes & FileAttributeDirectory) != 0)
        {
            return NtfsUsnRecordKind.Directory;
        }

        return attributes == 0 ? NtfsUsnRecordKind.Other : NtfsUsnRecordKind.File;
    }

    private static uint AlignUp8(uint length) => (length + 7u) & ~7u;

    private static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> span, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, sizeof(uint)));

    private static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> span, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, sizeof(ushort)));

    private static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> span, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, sizeof(ulong)));
}
internal sealed class NtfsUsnRecordParseCounters
{
    public long TotalRawSlots { get; set; }

    public long UnsupportedVersionRecords { get; set; }

    public long InvalidRecords { get; set; }
}
