using System.Buffers.Binary;
using System.Text;
using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Native;

namespace WinDirCleaner.Core.Tests;

public sealed class NtfsUsnRecordParserTests
{
    [Fact]
    public void TryParseSingleRecord_V2_ReadsFrnParentAndName()
    {
        var bytes = BuildV2UsnRecord(0x1000UL, 0x2000UL, 0x00000080, "Hello.txt");
        Assert.True(NtfsUsnRecordParser.TryParseSingleRecordForTests(bytes, out var rec));
        Assert.NotNull(rec);
        Assert.Equal("0000000000001000", rec!.FileReferenceNumber);
        Assert.Equal("0000000000002000", rec.ParentFileReferenceNumber);
        Assert.Equal("Hello.txt", rec.Name);
        Assert.Equal(2, rec.MajorVersion);
        Assert.Equal(NtfsUsnRecordKind.File, rec.Kind);
    }

    [Fact]
    public void TryParseSingleRecord_DirectoryAttribute_YieldsDirectoryKind()
    {
        const uint fileAttributeDirectory = 0x00000010;
        var bytes = BuildV2UsnRecord(3, 4, fileAttributeDirectory, "Subdir");
        Assert.True(NtfsUsnRecordParser.TryParseSingleRecordForTests(bytes, out var rec));
        Assert.Equal(NtfsUsnRecordKind.Directory, rec!.Kind);
    }

    [Fact]
    public void TryParseSingleRecord_ReparsePointAttribute_YieldsReparseKind_EvenWithDirectoryBit()
    {
        const uint dir = 0x00000010;
        const uint reparse = 0x00000400;
        var bytes = BuildV2UsnRecord(5, 6, dir | reparse, "JunctionOrLink");
        Assert.True(NtfsUsnRecordParser.TryParseSingleRecordForTests(bytes, out var rec));
        Assert.Equal(NtfsUsnRecordKind.ReparsePoint, rec!.Kind);
    }

    [Fact]
    public void ParseUsnDataBuffer_InvalidRecordLength_IncrementsInvalidAndStops()
    {
        var buf = new byte[8 + 8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(0), 0xDEADBEEFUL);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), 10_000);
        var c = new NtfsUsnRecordParseCounters();
        var dict = new Dictionary<string, NtfsFileRecord>(StringComparer.Ordinal);
        var samples = new List<NtfsFileRecord>();
        var roots = new List<string>();
        NtfsUsnRecordParser.ParseUsnDataBuffer(buf, buf.Length, c, dict, samples, roots, 50, 20);
        Assert.Equal(1, c.TotalRawSlots);
        Assert.Equal(1, c.InvalidRecords);
        Assert.Empty(dict);
    }

    [Fact]
    public void ParseUsnDataBuffer_EightBytePrefixOnly_DoesNotTreatPrefixAsRecord()
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(0), 0x123456789ABCDEF0UL);
        var c = new NtfsUsnRecordParseCounters();
        var dict = new Dictionary<string, NtfsFileRecord>(StringComparer.Ordinal);
        NtfsUsnRecordParser.ParseUsnDataBuffer(buf, buf.Length, c, dict, new List<NtfsFileRecord>(), new List<string>(), 50, 20);
        Assert.Equal(0, c.TotalRawSlots);
        Assert.Empty(dict);
    }

    [Fact]
    public void ParseUsnDataBuffer_SkipsEightByteStartFrnBeforeRecords()
    {
        var rec = BuildV2UsnRecord(0xABCDUL, 0x500UL, 0x80, "AfterPrefix");
        var buf = new byte[8 + rec.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(0), 0xFFFFFFFFFFFFFFFFUL);
        rec.CopyTo(buf.AsSpan(8));
        var c = new NtfsUsnRecordParseCounters();
        var dict = new Dictionary<string, NtfsFileRecord>(StringComparer.Ordinal);
        NtfsUsnRecordParser.ParseUsnDataBuffer(buf, buf.Length, c, dict, new List<NtfsFileRecord>(), new List<string>(), 50, 20);
        Assert.Equal(1, c.TotalRawSlots);
        Assert.Single(dict);
        Assert.Equal("AfterPrefix", dict.Values.First().Name);
    }

    [Fact]
    public void ParseUsnDataBuffer_MajorVersion3_CountsUnsupported()
    {
        var rec = BuildV2UsnRecord(9, 10, 0x80, "V3ish", majorVersion: 3);
        var buf = new byte[8 + rec.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(0), 0UL);
        rec.CopyTo(buf.AsSpan(8));
        var c = new NtfsUsnRecordParseCounters();
        var dict = new Dictionary<string, NtfsFileRecord>(StringComparer.Ordinal);
        NtfsUsnRecordParser.ParseUsnDataBuffer(buf, buf.Length, c, dict, new List<NtfsFileRecord>(), new List<string>(), 50, 20);
        Assert.Equal(1, c.TotalRawSlots);
        Assert.Equal(1, c.UnsupportedVersionRecords);
        Assert.Empty(dict);
    }

    private static byte[] BuildV2UsnRecord(
        ulong fileReferenceNumber,
        ulong parentFileReferenceNumber,
        uint fileAttributes,
        string fileName,
        ushort majorVersion = 2)
    {
        var nameUtf16 = Encoding.Unicode.GetBytes(fileName);
        var nameLen = nameUtf16.Length;
        var bodyLen = 60 + nameLen;
        var paddedLen = (bodyLen + 7) & ~7;
        var b = new byte[paddedLen];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), (uint)paddedLen);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), majorVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8), fileReferenceNumber);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(16), parentFileReferenceNumber);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(24), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(32), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(40), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(44), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(48), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52), fileAttributes);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(56), (ushort)nameLen);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(58), 60);
        nameUtf16.CopyTo(b.AsSpan(60));
        return b;
    }
}
