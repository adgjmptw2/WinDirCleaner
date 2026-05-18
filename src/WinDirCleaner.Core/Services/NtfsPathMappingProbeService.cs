using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Native;

namespace WinDirCleaner.Core.Services;

public sealed class NtfsPathMappingProbeService : INtfsPathMappingProbeService
{
    private const int OutputBufferBytes = 64 * 1024;

    private const int MaxIoctlIterations = 200_000;

    private const uint ErrorAccessDenied = 5;

    private const uint ErrorNotSupported = 50;

    private const uint ErrorInvalidParameter = 87;

    private const uint ErrorHandleEof = 38;

    private const uint ErrorJournalNotActive = 1171;

    public Task<NtfsPathMappingProbeResult> ProbePathMappingAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("targetPath is required.", nameof(targetPath));
        }

        return Task.Run(() => ProbeCore(targetPath.Trim(), cancellationToken), cancellationToken);
    }

    internal static bool TryMatchParentChainToVolumeRoot(
        NtfsFileRecord leafCandidate,
        IReadOnlyList<string> segmentsFromDriveRoot,
        IDictionary<string, NtfsFileRecord> recordsByFrn)
    {
        if (segmentsFromDriveRoot.Count == 0)
        {
            return false;
        }

        if (!NameEquals(leafCandidate.Name, segmentsFromDriveRoot[^1]))
        {
            return false;
        }

        var idx = segmentsFromDriveRoot.Count - 2;
        var node = leafCandidate;

        while (idx >= 0)
        {
            if (!recordsByFrn.TryGetValue(node.ParentFileReferenceNumber, out var parent))
            {
                return false;
            }

            if (!NameEquals(parent.Name, segmentsFromDriveRoot[idx]))
            {
                return false;
            }

            node = parent;
            idx--;
        }

        return NtfsUsnRecordParser.IsLikelyVolumeRootParentFrn(node.ParentFileReferenceNumber);
    }

    private static bool NameEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static NtfsPathMappingProbeResult ProbeCore(string targetPath, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var inputPath = targetPath;

        try
        {
            return ProbeCoreInner(inputPath, cancellationToken, sw);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Failed,
                inputPath,
                rootPath: "(n/a)",
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "작업이 취소되었습니다.",
                null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Failed,
                inputPath,
                rootPath: "(n/a)",
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "예기치 않은 오류가 발생했습니다.",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static NtfsPathMappingProbeResult ProbeCoreInner(
        string inputPath,
        CancellationToken cancellationToken,
        Stopwatch sw)
    {
        if (!OperatingSystem.IsWindows())
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Unsupported,
                inputPath,
                rootPath: "(n/a)",
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "Windows가 아닌 환경에서는 NTFS 볼륨 IOCTL을 사용할 수 없습니다.",
                "OperatingSystem.IsWindows() == false");
        }

        string expanded;
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(inputPath);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Failed,
                inputPath,
                rootPath: "(n/a)",
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "경로를 확장하지 못했습니다.",
                ex.Message);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(expanded);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Failed,
                inputPath,
                rootPath: "(n/a)",
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "전체 경로를 만들지 못했습니다.",
                ex.Message);
        }

        if (fullPath.StartsWith("\\\\", StringComparison.Ordinal) && !fullPath.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Unsupported,
                inputPath,
                rootPath: "(n/a)",
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "이 PoC에서는 UNC 경로를 지원하지 않습니다.",
                null);
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.PathNotFound,
                inputPath,
                rootPath: "(n/a)",
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "대상 경로가 존재하지 않습니다.",
                fullPath);
        }

        var driveRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(driveRoot))
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Failed,
                inputPath,
                rootPath: "(n/a)",
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "드라이브 루트를 확인할 수 없습니다.",
                null);
        }

        var rootNorm = NormalizeComparablePath(driveRoot);
        var fullNorm = NormalizeComparablePath(fullPath);

        if (!fullNorm.StartsWith(rootNorm, StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Unsupported,
                inputPath,
                rootNorm,
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "볼륨 루트와 경로의 관계를 해석하지 못했습니다.",
                fullNorm);
        }

        if (!TryGetSegmentsUnderDriveRoot(fullNorm, rootNorm, out var segments))
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Unsupported,
                inputPath,
                rootNorm,
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "볼륨 루트만 선택된 경우 이 진단에서는 매핑 대상을 한정할 수 없습니다.",
                null);
        }

        var driveLetter = char.ToUpperInvariant(rootNorm.TrimEnd('\\')[0]);
        if (driveLetter < 'A' || driveLetter > 'Z')
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Failed,
                inputPath,
                rootNorm,
                volumePath: "(n/a)",
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "드라이브 문자를 확인할 수 없습니다.",
                null);
        }

        var volumePath = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"\\\\.\\{driveLetter}:");

        DriveInfo driveInfo;
        try
        {
            driveInfo = new DriveInfo(new string(new[] { driveLetter, ':' }));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.Failed,
                inputPath,
                rootNorm,
                volumePath,
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                "DriveInfo를 생성하지 못했습니다.",
                ex.Message);
        }

        if (!string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            return Fail(
                NtfsPathMappingStatus.NotNtfs,
                inputPath,
                rootNorm,
                volumePath,
                isNtfs: false,
                0,
                0,
                sw.Elapsed,
                $"파일 시스템이 NTFS가 아닙니다: {driveInfo.DriveFormat}",
                null);
        }

        using var volumeHandle = NtfsFastScanVolumeHelper.OpenVolumeReadOnly(volumePath);
        if (volumeHandle is null)
        {
            var err = Marshal.GetLastWin32Error();
            sw.Stop();
            var st = err == ErrorAccessDenied ? NtfsPathMappingStatus.AccessDenied : NtfsPathMappingStatus.Failed;
            return Fail(
                st,
                inputPath,
                rootNorm,
                volumePath,
                isNtfs: true,
                0,
                0,
                sw.Elapsed,
                $"볼륨을 읽기 전용으로 열지 못했습니다. Win32 오류 {err}.",
                null);
        }

        var parseCounters = new NtfsUsnRecordParseCounters();
        var recordsByFrn = new Dictionary<string, NtfsFileRecord>(StringComparer.Ordinal);
        var sampleRecords = new List<NtfsFileRecord>(0);
        var sampleRootNames = new List<string>(0);

        var mft = new WindowsNativeMethods.MftEnumDataV0
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = long.MaxValue,
        };

        var outBuffer = new byte[OutputBufferBytes];
        var inSize = Marshal.SizeOf<WindowsNativeMethods.MftEnumDataV0>();
        var ioctlCount = 0;
        string? detail = null;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (++ioctlCount > MaxIoctlIterations)
                {
                    detail = $"IOCTL 반복 상한에 도달해 중단했습니다({MaxIoctlIterations}회).";
                    break;
                }

                if (!WindowsNativeMethods.DeviceIoControl(
                        volumeHandle,
                        WindowsNativeMethods.FsctlEnumUsnData,
                        ref mft,
                        inSize,
                        outBuffer,
                        outBuffer.Length,
                        out var bytesReturned,
                        IntPtr.Zero))
                {
                    var err = (uint)Marshal.GetLastWin32Error();
                    if (err == ErrorHandleEof)
                    {
                        NtfsUsnRecordParser.ParseUsnDataBuffer(
                            outBuffer,
                            bytesReturned,
                            parseCounters,
                            recordsByFrn,
                            sampleRecords,
                            sampleRootNames,
                            maxSampleRecords: 0,
                            maxSampleRootNames: 0);
                        break;
                    }

                    if (err == ErrorJournalNotActive)
                    {
                        sw.Stop();
                        return Fail(
                            NtfsPathMappingStatus.Failed,
                            inputPath,
                            rootNorm,
                            volumePath,
                            isNtfs: true,
                            parseCounters.TotalRawSlots,
                            recordsByFrn.Count,
                            sw.Elapsed,
                            "USN 변경 저널이 비활성 상태이거나 FSCTL_ENUM_USN_DATA를 사용할 수 없습니다.",
                            $"Win32 오류 {err}");
                    }

                    if (err == ErrorAccessDenied)
                    {
                        sw.Stop();
                        return Fail(
                            NtfsPathMappingStatus.AccessDenied,
                            inputPath,
                            rootNorm,
                            volumePath,
                            isNtfs: true,
                            parseCounters.TotalRawSlots,
                            recordsByFrn.Count,
                            sw.Elapsed,
                            $"DeviceIoControl(FSCTL_ENUM_USN_DATA) 접근이 거부되었습니다. Win32 오류 {err}.",
                            null);
                    }

                    if (err == ErrorNotSupported || err == ErrorInvalidParameter)
                    {
                        sw.Stop();
                        return Fail(
                            NtfsPathMappingStatus.Unsupported,
                            inputPath,
                            rootNorm,
                            volumePath,
                            isNtfs: true,
                            parseCounters.TotalRawSlots,
                            recordsByFrn.Count,
                            sw.Elapsed,
                            $"FSCTL_ENUM_USN_DATA가 지원되지 않거나 입력이 거부되었습니다. Win32 오류 {err}.",
                            null);
                    }

                    sw.Stop();
                    return Fail(
                        NtfsPathMappingStatus.Failed,
                        inputPath,
                        rootNorm,
                        volumePath,
                        isNtfs: true,
                        parseCounters.TotalRawSlots,
                        recordsByFrn.Count,
                        sw.Elapsed,
                        $"DeviceIoControl(FSCTL_ENUM_USN_DATA) 실패. Win32 오류 {err}.",
                        null);
                }

                const int frnPrefixBytes = 8;
                if (bytesReturned < frnPrefixBytes)
                {
                    break;
                }

                var nextStart = BitConverter.ToUInt64(outBuffer, 0);
                if (nextStart == ulong.MaxValue)
                {
                    NtfsUsnRecordParser.ParseUsnDataBuffer(
                        outBuffer,
                        bytesReturned,
                        parseCounters,
                        recordsByFrn,
                        sampleRecords,
                        sampleRootNames,
                        maxSampleRecords: 0,
                        maxSampleRootNames: 0);
                    break;
                }

                NtfsUsnRecordParser.ParseUsnDataBuffer(
                    outBuffer,
                    bytesReturned,
                    parseCounters,
                    recordsByFrn,
                    sampleRecords,
                    sampleRootNames,
                    maxSampleRecords: 0,
                    maxSampleRootNames: 0);

                var batchCount = NtfsUsnRecordParser.CountRawRecordSlots(outBuffer, bytesReturned, OutputBufferBytes);
                if (batchCount == 0 && bytesReturned > frnPrefixBytes)
                {
                    sw.Stop();
                    return Fail(
                        NtfsPathMappingStatus.Failed,
                        inputPath,
                        rootNorm,
                        volumePath,
                        isNtfs: true,
                        parseCounters.TotalRawSlots,
                        recordsByFrn.Count,
                        sw.Elapsed,
                        "출력 버퍼에서 USN_RECORD를 파싱하지 못했습니다.",
                        $"bytesReturned={bytesReturned}");
                }

                mft.StartFileReferenceNumber = nextStart;

                if (bytesReturned == frnPrefixBytes || batchCount == 0)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            throw;
        }

        sw.Stop();

        var scanned = parseCounters.TotalRawSlots;
        var parsed = (long)recordsByFrn.Count;
        var mergedDetail = detail is null
            ? $"FSCTL_ENUM_USN_DATA 루프 종료. IOCTL {ioctlCount}회. TotalRawSlots={scanned}, unique FRN={parsed}."
            : detail + $" IOCTL {ioctlCount}회.";

        var leafName = segments[^1];
        foreach (var cand in recordsByFrn.Values)
        {
            if (!NameEquals(cand.Name, leafName))
            {
                continue;
            }

            if (TryMatchParentChainToVolumeRoot(cand, segments, recordsByFrn))
            {
                return new NtfsPathMappingProbeResult(
                    NtfsPathMappingStatus.Completed,
                    inputPath,
                    rootNorm,
                    volumePath,
                    isNtfs: true,
                    scanned,
                    parsed,
                    sw.Elapsed,
                    cand.FileReferenceNumber,
                    cand.Name,
                    null,
                    mergedDetail + " 마지막 세그먼트 + ParentFRN 체인으로 일치하는 레코드를 찾았습니다.");
            }
        }

        return new NtfsPathMappingProbeResult(
            NtfsPathMappingStatus.PathNotFound,
            inputPath,
            rootNorm,
            volumePath,
            isNtfs: true,
            scanned,
            parsed,
            sw.Elapsed,
            null,
            null,
            "세그먼트 기준으로 FRN 트리에서 일치하는 경로를 찾지 못했습니다.",
            mergedDetail);
    }

    private static string NormalizeComparablePath(string path)
    {
        var p = path.TrimEnd().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (p.Length >= 2 && p[1] == ':')
        {
            var last = p[^1];
            if (last != Path.DirectorySeparatorChar)
            {
                p += Path.DirectorySeparatorChar;
            }
        }

        return p;
    }

    private static bool TryGetSegmentsUnderDriveRoot(string fullNorm, string rootNorm, out List<string> segments)
    {
        segments = new List<string>();
        if (fullNorm.Length < rootNorm.Length)
        {
            return false;
        }

        var rel = fullNorm.AsSpan(rootNorm.Length).Trim(Path.DirectorySeparatorChar);
        if (rel.IsEmpty)
        {
            return false;
        }

        foreach (var piece in rel.ToString().Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (piece.Length == 0)
            {
                continue;
            }

            if (piece is "." or "..")
            {
                return false;
            }

            segments.Add(piece);
        }

        return segments.Count > 0;
    }

    private static NtfsPathMappingProbeResult Fail(
        NtfsPathMappingStatus status,
        string inputPath,
        string rootPath,
        string volumePath,
        bool isNtfs,
        long recordsScanned,
        long parsedRecords,
        TimeSpan elapsed,
        string? errorMessage,
        string? detailMessage) =>
        new(
            status,
            inputPath,
            string.IsNullOrWhiteSpace(rootPath) ? "(n/a)" : rootPath,
            string.IsNullOrWhiteSpace(volumePath) ? "(n/a)" : volumePath,
            isNtfs,
            recordsScanned < 0 ? 0 : recordsScanned,
            parsedRecords < 0 ? 0 : parsedRecords,
            elapsed,
            null,
            null,
            errorMessage,
            detailMessage);
}
