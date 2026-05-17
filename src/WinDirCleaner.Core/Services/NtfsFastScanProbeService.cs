using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Native;

namespace WinDirCleaner.Core.Services;

public sealed class NtfsFastScanProbeService : INtfsFastScanProbeService
{
    private const int OutputBufferBytes = 64 * 1024;

    private const int MaxIoctlIterations = 200_000;

    private const uint ErrorAccessDenied = 5;

    private const uint ErrorNotSupported = 50;

    private const uint ErrorInvalidParameter = 87;

    private const uint ErrorHandleEof = 38;

    private const uint ErrorJournalNotActive = 1171;

    public Task<NtfsFastScanProbeResult> ProbeAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        return Task.Run(() => ProbeCore(rootPath, cancellationToken), cancellationToken);
    }

    private static NtfsFastScanProbeResult ProbeCore(string rootPath, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return ProbeCoreInner(rootPath, cancellationToken, sw);
        }
        catch (OperationCanceledException)
        {
            return Finish(
                NtfsFastScanStatus.Failed,
                rootPath.Trim(),
                @"\\.\(n/a)",
                isNtfs: false,
                recordsRead: 0,
                sw,
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
                recordsRead: 0,
                sw,
                "예기치 않은 오류가 발생했습니다.",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static NtfsFastScanProbeResult ProbeCoreInner(
        string rootPath,
        CancellationToken cancellationToken,
        Stopwatch sw)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Finish(
                NtfsFastScanStatus.ApiUnavailable,
                rootPath,
                volumePath: string.Empty,
                isNtfs: false,
                recordsRead: 0,
                sw,
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
                recordsRead: 0,
                sw,
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
                recordsRead: 0,
                sw,
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
                recordsRead: 0,
                sw,
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
                recordsRead: 0,
                sw,
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
                recordsRead: 0,
                sw,
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
                recordsRead: 0,
                sw,
                $"볼륨을 읽기 전용으로 열지 못했습니다. Win32 오류 {err}.",
                null);
        }

        long totalRecords = 0;
        var mft = new WindowsNativeMethods.MftEnumDataV0
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = long.MaxValue,
        };

        var outBuffer = new byte[OutputBufferBytes];
        var inSize = Marshal.SizeOf<WindowsNativeMethods.MftEnumDataV0>();
        var ioctlCount = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (++ioctlCount > MaxIoctlIterations)
                {
                    return Finish(
                        NtfsFastScanStatus.Completed,
                        normalizedRoot,
                        volumePath,
                        isNtfs: true,
                        totalRecords,
                        sw,
                        null,
                        $"IOCTL 반복 상한에 도달해 중단했습니다({MaxIoctlIterations}회).");
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
                        break;
                    }

                    if (err == ErrorJournalNotActive)
                    {
                        return Finish(
                            NtfsFastScanStatus.Failed,
                            normalizedRoot,
                            volumePath,
                            isNtfs: true,
                            totalRecords,
                            sw,
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
                            totalRecords,
                            sw,
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
                            totalRecords,
                            sw,
                            $"FSCTL_ENUM_USN_DATA가 지원되지 않거나 입력이 거부되었습니다. Win32 오류 {err}.",
                            null);
                    }

                    return Finish(
                        NtfsFastScanStatus.Failed,
                        normalizedRoot,
                        volumePath,
                        isNtfs: true,
                        totalRecords,
                        sw,
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
                    totalRecords += NtfsUsnRecordParser.CountRawRecordSlots(outBuffer, bytesReturned, OutputBufferBytes);
                    break;
                }

                var batchCount = NtfsUsnRecordParser.CountRawRecordSlots(outBuffer, bytesReturned, OutputBufferBytes);
                if (batchCount == 0 && bytesReturned > frnPrefixBytes)
                {
                    return Finish(
                        NtfsFastScanStatus.Failed,
                        normalizedRoot,
                        volumePath,
                        isNtfs: true,
                        totalRecords,
                        sw,
                        "출력 버퍼에서 USN_RECORD를 파싱하지 못했습니다.",
                        $"bytesReturned={bytesReturned}");
                }

                totalRecords += batchCount;
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
                totalRecords,
                sw,
                "작업이 취소되었습니다.",
                null);
        }

        return Finish(
            NtfsFastScanStatus.Completed,
            normalizedRoot,
            volumePath,
            isNtfs: true,
            totalRecords,
            sw,
            null,
            $"FSCTL_ENUM_USN_DATA 완료. IOCTL {ioctlCount}회, 레코드 {totalRecords}건.");
    }

    private static NtfsFastScanProbeResult Finish(
        NtfsFastScanStatus status,
        string rootPath,
        string volumePath,
        bool isNtfs,
        long recordsRead,
        Stopwatch sw,
        string? errorMessage,
        string? detailMessage)
    {
        sw.Stop();
        return new NtfsFastScanProbeResult(
            status,
            rootPath,
            string.IsNullOrWhiteSpace(volumePath) ? @"\\.\(n/a)" : volumePath,
            isNtfs,
            recordsRead,
            sw.Elapsed,
            errorMessage,
            detailMessage);
    }
}
