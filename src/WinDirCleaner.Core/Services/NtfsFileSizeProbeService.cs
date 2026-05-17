using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Native;

namespace WinDirCleaner.Core.Services;

/// <summary>USN에서 고른 파일 일부에 대해 OpenFileById + GetFileSizeEx로 크기 조회를 시도합니다. 전 볼륨 용량 집계는 하지 않습니다.</summary>
public sealed class NtfsFileSizeProbeService : INtfsFileSizeProbeService
{
    public const int MaxSampleCount = 50_000;

    private const int OutputBufferBytes = 64 * 1024;

    private const int MaxIoctlIterations = 200_000;

    private const int MaxResultSamples = 40;

    private const uint ErrorAccessDenied = 5;

    private const uint ErrorFileNotFound = 2;

    private const uint ErrorPathNotFound = 3;

    private const uint ErrorNotSupported = 50;

    private const uint ErrorInvalidParameter = 87;

    private const uint ErrorHandleEof = 38;

    private const uint ErrorJournalNotActive = 1171;

    public Task<NtfsFileSizeProbeResult> ProbeFileSizesAsync(
        string rootPath,
        int sampleCount = 500,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        var clamped = Math.Clamp(sampleCount, 1, MaxSampleCount);
        return Task.Run(() => ProbeCore(rootPath, clamped, cancellationToken), cancellationToken);
    }

    private static NtfsFileSizeProbeResult ProbeCore(string rootPath, int sampleCount, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return ProbeCoreInner(rootPath, sampleCount, cancellationToken, sw);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return Finish(
                NtfsFastScanStatus.Failed,
                rootPath.Trim(),
                @"\\.\(n/a)",
                isNtfs: false,
                EmptySummary(sampleCount, sw),
                Array.Empty<NtfsFileSizeProbeSample>(),
                "작업이 취소되었습니다.",
                null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Finish(
                NtfsFastScanStatus.Failed,
                rootPath.Trim(),
                @"\\.\(n/a)",
                isNtfs: false,
                EmptySummary(sampleCount, sw),
                Array.Empty<NtfsFileSizeProbeSample>(),
                "예기치 않은 오류가 발생했습니다.",
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static NtfsFileSizeProbeResult ProbeCoreInner(
        string rootPath,
        int sampleCount,
        CancellationToken cancellationToken,
        Stopwatch sw)
    {
        if (!OperatingSystem.IsWindows())
        {
            sw.Stop();
            return Finish(
                NtfsFastScanStatus.ApiUnavailable,
                rootPath.Trim(),
                volumePath: string.Empty,
                isNtfs: false,
                EmptySummary(sampleCount, sw),
                Array.Empty<NtfsFileSizeProbeSample>(),
                "Windows가 아닌 환경에서는 사용할 수 없습니다.",
                null);
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = NtfsFastScanVolumeHelper.NormalizeRootDirectory(rootPath);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Finish(
                NtfsFastScanStatus.Failed,
                rootPath.Trim(),
                volumePath: string.Empty,
                isNtfs: false,
                EmptySummary(sampleCount, sw),
                Array.Empty<NtfsFileSizeProbeSample>(),
                "루트 경로를 해석하지 못했습니다.",
                ex.Message);
        }

        var driveRoot = Path.GetPathRoot(normalizedRoot);
        if (string.IsNullOrEmpty(driveRoot))
        {
            sw.Stop();
            return Finish(
                NtfsFastScanStatus.Failed,
                normalizedRoot,
                @"\\.\(n/a)",
                isNtfs: false,
                EmptySummary(sampleCount, sw),
                Array.Empty<NtfsFileSizeProbeSample>(),
                "드라이브 루트를 확인할 수 없습니다.",
                null);
        }

        var driveLetter = char.ToUpperInvariant(driveRoot.TrimEnd('\\', '/')[0]);
        if (driveLetter < 'A' || driveLetter > 'Z')
        {
            sw.Stop();
            return Finish(
                NtfsFastScanStatus.Failed,
                normalizedRoot,
                volumePath: string.Empty,
                isNtfs: false,
                EmptySummary(sampleCount, sw),
                Array.Empty<NtfsFileSizeProbeSample>(),
                "드라이브 문자를 확인할 수 없습니다.",
                driveRoot);
        }

        var volumePath = string.Create(CultureInfo.InvariantCulture, $"\\\\.\\{driveLetter}:");

        DriveInfo driveInfo;
        try
        {
            driveInfo = new DriveInfo(new string(new[] { driveLetter, ':' }));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Finish(
                NtfsFastScanStatus.Failed,
                normalizedRoot,
                volumePath,
                isNtfs: false,
                EmptySummary(sampleCount, sw),
                Array.Empty<NtfsFileSizeProbeSample>(),
                "DriveInfo를 생성하지 못했습니다.",
                ex.Message);
        }

        if (!string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            return Finish(
                NtfsFastScanStatus.NotNtfs,
                normalizedRoot,
                volumePath,
                isNtfs: false,
                EmptySummary(sampleCount, sw),
                Array.Empty<NtfsFileSizeProbeSample>(),
                $"파일 시스템이 NTFS가 아닙니다: {driveInfo.DriveFormat}",
                null);
        }

        using var volumeHandle = NtfsFastScanVolumeHelper.OpenVolumeReadOnly(volumePath);
        if (volumeHandle is null)
        {
            var err = Marshal.GetLastWin32Error();
            sw.Stop();
            var status = err == ErrorAccessDenied ? NtfsFastScanStatus.AccessDenied : NtfsFastScanStatus.Failed;
            return Finish(
                status,
                normalizedRoot,
                volumePath,
                isNtfs: true,
                EmptySummary(sampleCount, sw),
                Array.Empty<NtfsFileSizeProbeSample>(),
                $"볼륨을 읽기 전용으로 열지 못했습니다. Win32 오류 {err}.",
                null);
        }

        var fileRecords = new List<NtfsFileRecord>(sampleCount);
        var seenFrn = new HashSet<string>(StringComparer.Ordinal);
        var parseCounters = new NtfsUsnRecordParseCounters();
        long volumeDistinctFileOrdinal = 0;
        var distinctStride = GetDistinctFileStride(sampleCount);
        var mft = new WindowsNativeMethods.MftEnumDataV0
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = long.MaxValue,
        };

        var outBuffer = new byte[OutputBufferBytes];
        var inSize = Marshal.SizeOf<WindowsNativeMethods.MftEnumDataV0>();
        var ioctlCount = 0;
        string? enumDetail = null;

        try
        {
            while (fileRecords.Count < sampleCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (++ioctlCount > MaxIoctlIterations)
                {
                    enumDetail = $"USN 열거 IOCTL 상한 도달({MaxIoctlIterations}회).";
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
                        NtfsUsnRecordParser.ParseUsnDataBufferCollectDistinctFileRecords(
                            outBuffer,
                            bytesReturned,
                            parseCounters,
                            fileRecords,
                            seenFrn,
                            sampleCount,
                            distinctStride,
                            ref volumeDistinctFileOrdinal);
                        break;
                    }

                    if (err == ErrorJournalNotActive)
                    {
                        sw.Stop();
                        return Finish(
                            NtfsFastScanStatus.Failed,
                            normalizedRoot,
                            volumePath,
                            isNtfs: true,
                            EmptySummary(sampleCount, sw),
                            Array.Empty<NtfsFileSizeProbeSample>(),
                            "USN 변경 저널이 비활성 상태이거나 FSCTL_ENUM_USN_DATA를 사용할 수 없습니다.",
                            $"Win32 오류 {err}.");
                    }

                    if (err == ErrorAccessDenied)
                    {
                        sw.Stop();
                        return Finish(
                            NtfsFastScanStatus.AccessDenied,
                            normalizedRoot,
                            volumePath,
                            isNtfs: true,
                            EmptySummary(sampleCount, sw),
                            Array.Empty<NtfsFileSizeProbeSample>(),
                            $"DeviceIoControl(FSCTL_ENUM_USN_DATA) 접근이 거부되었습니다. Win32 오류 {err}.",
                            null);
                    }

                    if (err == ErrorNotSupported || err == ErrorInvalidParameter)
                    {
                        sw.Stop();
                        return Finish(
                            NtfsFastScanStatus.ApiUnavailable,
                            normalizedRoot,
                            volumePath,
                            isNtfs: true,
                            EmptySummary(sampleCount, sw),
                            Array.Empty<NtfsFileSizeProbeSample>(),
                            $"FSCTL_ENUM_USN_DATA가 지원되지 않거나 입력이 거부되었습니다. Win32 오류 {err}.",
                            null);
                    }

                    sw.Stop();
                    return Finish(
                        NtfsFastScanStatus.Failed,
                        normalizedRoot,
                        volumePath,
                        isNtfs: true,
                        EmptySummary(sampleCount, sw),
                        Array.Empty<NtfsFileSizeProbeSample>(),
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
                    NtfsUsnRecordParser.ParseUsnDataBufferCollectDistinctFileRecords(
                        outBuffer,
                        bytesReturned,
                        parseCounters,
                        fileRecords,
                        seenFrn,
                        sampleCount,
                        distinctStride,
                        ref volumeDistinctFileOrdinal);
                    break;
                }

                NtfsUsnRecordParser.ParseUsnDataBufferCollectDistinctFileRecords(
                    outBuffer,
                    bytesReturned,
                    parseCounters,
                    fileRecords,
                    seenFrn,
                    sampleCount,
                    distinctStride,
                    ref volumeDistinctFileOrdinal);

                var batchCount = NtfsUsnRecordParser.CountRawRecordSlots(outBuffer, bytesReturned, OutputBufferBytes);
                if (batchCount == 0 && bytesReturned > frnPrefixBytes)
                {
                    sw.Stop();
                    return Finish(
                        NtfsFastScanStatus.Failed,
                        normalizedRoot,
                        volumePath,
                        isNtfs: true,
                        EmptySummary(sampleCount, sw),
                        Array.Empty<NtfsFileSizeProbeSample>(),
                        "출력 버퍼에서 USN_RECORD를 파싱하지 못했습니다.",
                        $"bytesReturned={bytesReturned}");
                }

                mft.StartFileReferenceNumber = nextStart;

                if (bytesReturned == frnPrefixBytes || batchCount == 0 || fileRecords.Count >= sampleCount)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return Finish(
                NtfsFastScanStatus.Failed,
                normalizedRoot,
                volumePath,
                isNtfs: true,
                EmptySummary(sampleCount, sw),
                Array.Empty<NtfsFileSizeProbeSample>(),
                "작업이 취소되었습니다.",
                null);
        }

        var displaySamples = new List<NtfsFileSizeProbeSample>(MaxResultSamples);
        var successCount = 0;
        var accessDeniedCount = 0;
        var notFoundCount = 0;
        var failedCount = 0;
        long totalSampledSizeBytes = 0;

        var share = WindowsNativeMethods.FileShareRead |
                    WindowsNativeMethods.FileShareWrite |
                    WindowsNativeMethods.FileShareDelete;

        var opensProcessed = 0;
        try
        {
            // One handle per file; cooperatively cancel between iterations.
            foreach (var rec in fileRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryParseFrnHex(rec.FileReferenceNumber, out var fileId))
                {
                    failedCount++;
                    TryAddSample(
                        displaySamples,
                        new NtfsFileSizeProbeSample(rec.FileReferenceNumber, rec.Name, false, null, "FRN 파싱 실패"));
                    opensProcessed++;
                    continue;
                }

                var desc = new WindowsNativeMethods.FileIdDescriptor
                {
                    dwSize = WindowsNativeMethods.FileIdDescriptorSizeBytes,
                    Type = WindowsNativeMethods.FileIdTypeFileId,
                    FileId = unchecked((long)fileId),
                };

                var fileHandle = WindowsNativeMethods.OpenFileById(
                    volumeHandle,
                    ref desc,
                    WindowsNativeMethods.FileReadAttributes,
                    share,
                    IntPtr.Zero,
                    0);

                if (fileHandle.IsInvalid)
                {
                    var err = (uint)Marshal.GetLastWin32Error();
                    fileHandle.Dispose();
                    ClassifyError(err, ref accessDeniedCount, ref notFoundCount, ref failedCount);
                    TryAddSample(
                        displaySamples,
                        new NtfsFileSizeProbeSample(
                            rec.FileReferenceNumber,
                            rec.Name,
                            false,
                            null,
                            $"Win32 오류 {err}"));
                    opensProcessed++;
                    continue;
                }

                using (fileHandle)
                {
                    if (!WindowsNativeMethods.GetFileSizeEx(fileHandle, out var size))
                    {
                        var err = (uint)Marshal.GetLastWin32Error();
                        ClassifyError(err, ref accessDeniedCount, ref notFoundCount, ref failedCount);
                        TryAddSample(
                            displaySamples,
                            new NtfsFileSizeProbeSample(
                                rec.FileReferenceNumber,
                                rec.Name,
                                false,
                                null,
                                $"GetFileSizeEx Win32 오류 {err}"));
                        opensProcessed++;
                        continue;
                    }

                    if (size < 0)
                    {
                        failedCount++;
                        TryAddSample(
                            displaySamples,
                            new NtfsFileSizeProbeSample(rec.FileReferenceNumber, rec.Name, false, null, "음수 크기"));
                        opensProcessed++;
                        continue;
                    }

                    successCount++;
                    totalSampledSizeBytes += size;
                    TryAddSample(
                        displaySamples,
                        new NtfsFileSizeProbeSample(rec.FileReferenceNumber, rec.Name, true, size, null));
                }

                opensProcessed++;
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            ComputeRates(opensProcessed, successCount, accessDeniedCount, notFoundCount, failedCount, out var sr, out var adr, out var fr);
            var elapsedPartial = sw.Elapsed;
            var fpsPartial = elapsedPartial.TotalSeconds > 0 ? opensProcessed / elapsedPartial.TotalSeconds : 0d;
            var partialSummary = new NtfsFileSizeProbeSummary(
                sampleCount,
                opensProcessed,
                successCount,
                accessDeniedCount,
                notFoundCount,
                failedCount,
                totalSampledSizeBytes,
                elapsedPartial,
                fpsPartial,
                sr,
                adr,
                fr);
            return Finish(
                NtfsFastScanStatus.Failed,
                normalizedRoot,
                volumePath,
                isNtfs: true,
                partialSummary,
                displaySamples,
                "작업이 취소되었습니다.",
                $"취소 시점까지 OpenFileById 시도 {opensProcessed}건 처리.");
        }

        sw.Stop();
        var attempted = fileRecords.Count;
        var elapsed = sw.Elapsed;
        var fps = elapsed.TotalSeconds > 0 ? attempted / elapsed.TotalSeconds : 0d;
        ComputeRates(attempted, successCount, accessDeniedCount, notFoundCount, failedCount, out var successRate, out var accessDeniedRate, out var failureRate);

        var summary = new NtfsFileSizeProbeSummary(
            sampleCount,
            attempted,
            successCount,
            accessDeniedCount,
            notFoundCount,
            failedCount,
            totalSampledSizeBytes,
            elapsed,
            fps,
            successRate,
            accessDeniedRate,
            failureRate);

        var detail = enumDetail is null
            ? $"USN에서 수집한 서로 다른 파일 레코드 {attempted}건에 대해 OpenFileById 시도. IOCTL {ioctlCount}회. distinct stride={distinctStride}."
            : enumDetail + $" IOCTL {ioctlCount}회, 수집 파일 레코드 {attempted}건, stride={distinctStride}.";

        return Finish(
            NtfsFastScanStatus.Completed,
            normalizedRoot,
            volumePath,
            isNtfs: true,
            summary,
            displaySamples,
            null,
            detail);
    }

    private static void ClassifyError(uint err, ref int accessDeniedCount, ref int notFoundCount, ref int failedCount)
    {
        if (err == ErrorAccessDenied)
        {
            accessDeniedCount++;
        }
        else if (err == ErrorFileNotFound || err == ErrorPathNotFound)
        {
            notFoundCount++;
        }
        else
        {
            failedCount++;
        }
    }

    private static int GetDistinctFileStride(int sampleCount) =>
        sampleCount >= 50_000 ? 10 :
        sampleCount >= 10_000 ? 5 :
        sampleCount >= 5_000 ? 3 : 1;

    private static void ComputeRates(
        int attempted,
        int successCount,
        int accessDeniedCount,
        int notFoundCount,
        int failedCount,
        out double successRate,
        out double accessDeniedRate,
        out double failureRate)
    {
        if (attempted <= 0)
        {
            successRate = 0;
            accessDeniedRate = 0;
            failureRate = 0;
            return;
        }

        successRate = Math.Clamp((double)successCount / attempted, 0, 1);
        accessDeniedRate = Math.Clamp((double)accessDeniedCount / attempted, 0, 1);
        failureRate = Math.Clamp((double)(notFoundCount + failedCount) / attempted, 0, 1);
    }

    private static void TryAddSample(List<NtfsFileSizeProbeSample> list, NtfsFileSizeProbeSample sample)
    {
        if (list.Count < MaxResultSamples)
        {
            list.Add(sample);
        }
    }

    private static bool TryParseFrnHex(string frnHex, out ulong fileId)
    {
        fileId = 0;
        if (string.IsNullOrWhiteSpace(frnHex))
        {
            return false;
        }

        return ulong.TryParse(
            frnHex.Trim(),
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out fileId);
    }

    private static NtfsFileSizeProbeSummary EmptySummary(int requestedSampleCount, Stopwatch sw)
    {
        return new NtfsFileSizeProbeSummary(
            requestedSampleCount,
            0,
            0,
            0,
            0,
            0,
            0,
            sw.Elapsed,
            0d,
            0d,
            0d,
            0d);
    }

    private static NtfsFileSizeProbeResult Finish(
        NtfsFastScanStatus status,
        string rootPath,
        string volumePath,
        bool isNtfs,
        NtfsFileSizeProbeSummary summary,
        IReadOnlyList<NtfsFileSizeProbeSample> samples,
        string? errorMessage,
        string? detailMessage)
    {
        return new NtfsFileSizeProbeResult(
            status,
            rootPath,
            string.IsNullOrWhiteSpace(volumePath) ? @"\\.\(n/a)" : volumePath,
            isNtfs,
            summary,
            samples,
            errorMessage,
            detailMessage);
    }
}
