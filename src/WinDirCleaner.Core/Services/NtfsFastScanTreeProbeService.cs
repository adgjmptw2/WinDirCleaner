using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Native;

namespace WinDirCleaner.Core.Services;

public sealed class NtfsFastScanTreeProbeService : INtfsFastScanTreeProbeService
{
    private const int OutputBufferBytes = 64 * 1024;

    private const int MaxIoctlIterations = 200_000;

    private const int MaxSampleRecords = 40;

    private const int MaxSampleRootNames = 20;

    private const uint ErrorAccessDenied = 5;

    private const uint ErrorNotSupported = 50;

    private const uint ErrorInvalidParameter = 87;

    private const uint ErrorHandleEof = 38;

    private const uint ErrorJournalNotActive = 1171;

    public Task<NtfsFastScanTreeProbeResult> ProbeTreeAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        return Task.Run(() => ProbeTreeCore(rootPath, cancellationToken), cancellationToken);
    }

    private static NtfsFastScanTreeProbeResult ProbeTreeCore(string rootPath, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return ProbeTreeCoreInner(rootPath, cancellationToken, sw);
        }
        catch (OperationCanceledException)
        {
            return Finish(
                NtfsFastScanStatus.Failed,
                rootPath.Trim(),
                @"\\.\(n/a)",
                isNtfs: false,
                EmptySummary(sw),
                Array.Empty<NtfsFileRecord>(),
                Array.Empty<string>(),
                "작업이 취소되었습니다.",
                null);
        }
        catch (Exception ex)
        {
            return Finish(
                NtfsFastScanStatus.Failed,
                rootPath.Trim(),
                @"\\.\(n/a)",
                isNtfs: false,
                EmptySummary(sw),
                Array.Empty<NtfsFileRecord>(),
                Array.Empty<string>(),
                "예기치 않은 오류가 발생했습니다.",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static NtfsFastScanTreeProbeResult ProbeTreeCoreInner(
        string rootPath,
        CancellationToken cancellationToken,
        Stopwatch sw)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Finish(
                NtfsFastScanStatus.ApiUnavailable,
                rootPath.Trim(),
                volumePath: string.Empty,
                isNtfs: false,
                EmptySummary(sw),
                Array.Empty<NtfsFileRecord>(),
                Array.Empty<string>(),
                "Windows가 아닌 환경에서는 NTFS 볼륨 IOCTL을 사용할 수 없습니다.",
                "OperatingSystem.IsWindows() == false");
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = NtfsFastScanVolumeHelper.NormalizeRootDirectory(rootPath);
        }
        catch (Exception ex)
        {
            return Finish(
                NtfsFastScanStatus.Failed,
                rootPath.Trim(),
                volumePath: string.Empty,
                isNtfs: false,
                EmptySummary(sw),
                Array.Empty<NtfsFileRecord>(),
                Array.Empty<string>(),
                "루트 경로를 해석하지 못했습니다.",
                ex.Message);
        }

        var driveRoot = Path.GetPathRoot(normalizedRoot);
        if (string.IsNullOrEmpty(driveRoot))
        {
            return Finish(
                NtfsFastScanStatus.Failed,
                normalizedRoot,
                @"\\.\(n/a)",
                isNtfs: false,
                EmptySummary(sw),
                Array.Empty<NtfsFileRecord>(),
                Array.Empty<string>(),
                "드라이브 루트를 확인할 수 없습니다.",
                null);
        }

        var driveLetter = char.ToUpperInvariant(driveRoot.TrimEnd('\\', '/')[0]);
        if (driveLetter < 'A' || driveLetter > 'Z')
        {
            return Finish(
                NtfsFastScanStatus.Failed,
                normalizedRoot,
                volumePath: string.Empty,
                isNtfs: false,
                EmptySummary(sw),
                Array.Empty<NtfsFileRecord>(),
                Array.Empty<string>(),
                "드라이브 문자를 확인할 수 없습니다.",
                driveRoot);
        }

        var volumePath = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"\\\\.\\{driveLetter}:");

        DriveInfo driveInfo;
        try
        {
            driveInfo = new DriveInfo(new string(new[] { driveLetter, ':' }));
        }
        catch (Exception ex)
        {
            return Finish(
                NtfsFastScanStatus.Failed,
                normalizedRoot,
                volumePath,
                isNtfs: false,
                EmptySummary(sw),
                Array.Empty<NtfsFileRecord>(),
                Array.Empty<string>(),
                "DriveInfo를 생성하지 못했습니다.",
                ex.Message);
        }

        if (!string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            return Finish(
                NtfsFastScanStatus.NotNtfs,
                normalizedRoot,
                volumePath,
                isNtfs: false,
                EmptySummary(sw),
                Array.Empty<NtfsFileRecord>(),
                Array.Empty<string>(),
                $"파일 시스템이 NTFS가 아닙니다: {driveInfo.DriveFormat}",
                null);
        }

        using var volumeHandle = NtfsFastScanVolumeHelper.OpenVolumeReadOnly(volumePath);
        if (volumeHandle is null)
        {
            var err = Marshal.GetLastWin32Error();
            var status = err == ErrorAccessDenied ? NtfsFastScanStatus.AccessDenied : NtfsFastScanStatus.Failed;
            return Finish(
                status,
                normalizedRoot,
                volumePath,
                isNtfs: true,
                EmptySummary(sw),
                Array.Empty<NtfsFileRecord>(),
                Array.Empty<string>(),
                $"볼륨을 읽기 전용으로 열지 못했습니다. Win32 오류 {err}.",
                null);
        }

        var parseCounters = new NtfsUsnRecordParseCounters();
        var recordsByFrn = new Dictionary<string, NtfsFileRecord>(StringComparer.Ordinal);
        var sampleRecords = new List<NtfsFileRecord>(MaxSampleRecords);
        var sampleRootNames = new List<string>(MaxSampleRootNames);

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
                            MaxSampleRecords,
                            MaxSampleRootNames);
                        break;
                    }

                    if (err == ErrorJournalNotActive)
                    {
                        return Finish(
                            NtfsFastScanStatus.Failed,
                            normalizedRoot,
                            volumePath,
                            isNtfs: true,
                            BuildSummary(sw, parseCounters, recordsByFrn),
                            sampleRecords,
                            sampleRootNames,
                            "USN 변경 저널이 비활성 상태이거나 FSCTL_ENUM_USN_DATA를 사용할 수 없습니다.",
                            $"Win32 오류 {err} (ERROR_JOURNAL_NOT_ACTIVE 등으로 해석 가능).");
                    }

                    if (err == ErrorAccessDenied)
                    {
                        return Finish(
                            NtfsFastScanStatus.AccessDenied,
                            normalizedRoot,
                            volumePath,
                            isNtfs: true,
                            BuildSummary(sw, parseCounters, recordsByFrn),
                            sampleRecords,
                            sampleRootNames,
                            $"DeviceIoControl(FSCTL_ENUM_USN_DATA) 접근이 거부되었습니다. Win32 오류 {err}.",
                            null);
                    }

                    if (err == ErrorNotSupported || err == ErrorInvalidParameter)
                    {
                        return Finish(
                            NtfsFastScanStatus.ApiUnavailable,
                            normalizedRoot,
                            volumePath,
                            isNtfs: true,
                            BuildSummary(sw, parseCounters, recordsByFrn),
                            sampleRecords,
                            sampleRootNames,
                            $"FSCTL_ENUM_USN_DATA가 지원되지 않거나 입력이 거부되었습니다. Win32 오류 {err}.",
                            null);
                    }

                    return Finish(
                        NtfsFastScanStatus.Failed,
                        normalizedRoot,
                        volumePath,
                        isNtfs: true,
                        BuildSummary(sw, parseCounters, recordsByFrn),
                        sampleRecords,
                        sampleRootNames,
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
                        MaxSampleRecords,
                        MaxSampleRootNames);
                    break;
                }

                NtfsUsnRecordParser.ParseUsnDataBuffer(
                    outBuffer,
                    bytesReturned,
                    parseCounters,
                    recordsByFrn,
                    sampleRecords,
                    sampleRootNames,
                    MaxSampleRecords,
                    MaxSampleRootNames);

                var batchCount = NtfsUsnRecordParser.CountRawRecordSlots(outBuffer, bytesReturned, OutputBufferBytes);
                if (batchCount == 0 && bytesReturned > frnPrefixBytes)
                {
                    return Finish(
                        NtfsFastScanStatus.Failed,
                        normalizedRoot,
                        volumePath,
                        isNtfs: true,
                        BuildSummary(sw, parseCounters, recordsByFrn),
                        sampleRecords,
                        sampleRootNames,
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
            return Finish(
                NtfsFastScanStatus.Failed,
                normalizedRoot,
                volumePath,
                isNtfs: true,
                BuildSummary(sw, parseCounters, recordsByFrn),
                sampleRecords,
                sampleRootNames,
                "작업이 취소되었습니다.",
                null);
        }

        var summary = BuildSummary(sw, parseCounters, recordsByFrn);
        var mergedDetail = detail is null
            ? $"FSCTL_ENUM_USN_DATA 루프 종료. IOCTL 호출 {ioctlCount}회. TotalRawSlots={summary.TotalRecords}, unique FRN={summary.ParsedRecords}."
            : detail + $" IOCTL 호출 {ioctlCount}회.";

        return Finish(
            NtfsFastScanStatus.Completed,
            normalizedRoot,
            volumePath,
            isNtfs: true,
            summary,
            sampleRecords,
            sampleRootNames,
            null,
            mergedDetail);
    }

    private static NtfsFastScanTreeSummary BuildSummary(
        Stopwatch sw,
        NtfsUsnRecordParseCounters parseCounters,
        Dictionary<string, NtfsFileRecord> recordsByFrn)
    {
        sw.Stop();
        var totalRaw = parseCounters.TotalRawSlots;
        var parsed = recordsByFrn.Count;

        long files = 0, dirs = 0, reparse = 0;
        foreach (var r in recordsByFrn.Values)
        {
            switch (r.Kind)
            {
                case NtfsUsnRecordKind.File:
                    files++;
                    break;
                case NtfsUsnRecordKind.Directory:
                    dirs++;
                    break;
                case NtfsUsnRecordKind.ReparsePoint:
                    reparse++;
                    break;
            }
        }

        long linked = 0, orphan = 0, rootCand = 0, linkInvalid = 0;
        foreach (var r in recordsByFrn.Values)
        {
            var frn = r.FileReferenceNumber;
            var pfrn = r.ParentFileReferenceNumber;

            if (string.Equals(frn, pfrn, StringComparison.OrdinalIgnoreCase))
            {
                linkInvalid++;
                continue;
            }

            if (NtfsUsnRecordParser.IsLikelyVolumeRootParentFrn(pfrn))
            {
                rootCand++;
            }

            if (recordsByFrn.ContainsKey(pfrn))
            {
                linked++;
            }
            else
            {
                orphan++;
            }
        }

        var invalidTotal = parseCounters.InvalidRecords + linkInvalid;

        return new NtfsFastScanTreeSummary(
            totalRaw,
            parsed,
            files,
            dirs,
            reparse,
            parseCounters.UnsupportedVersionRecords,
            invalidTotal,
            linked,
            orphan,
            rootCand,
            sw.Elapsed);
    }

    private static NtfsFastScanTreeSummary EmptySummary(Stopwatch sw)
    {
        sw.Stop();
        return new NtfsFastScanTreeSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, sw.Elapsed);
    }

    private static NtfsFastScanTreeProbeResult Finish(
        NtfsFastScanStatus status,
        string rootPath,
        string volumePath,
        bool isNtfs,
        NtfsFastScanTreeSummary summary,
        IReadOnlyList<NtfsFileRecord> sampleRecords,
        IReadOnlyList<string> sampleRootNames,
        string? errorMessage,
        string? detailMessage)
    {
        return new NtfsFastScanTreeProbeResult(
            status,
            rootPath,
            string.IsNullOrWhiteSpace(volumePath) ? @"\\.\(n/a)" : volumePath,
            isNtfs,
            summary,
            sampleRecords,
            sampleRootNames,
            errorMessage,
            detailMessage);
    }
}
