using System.Diagnostics;
using System.Security;
using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

public sealed class StorageAnalysisService : IStorageAnalysisService
{
    private const string ReparseNote = "재분석 지점(정션/심볼릭 링크 등)으로 내부를 따라가지 않습니다.";
    private const string AccessNote = "접근 불가 또는 건너뜀";

    private const int MinFilesBetweenScanReports = 250;
    private const double MinMillisecondsBetweenScanReports = 400.0;

    private static readonly EnumerationOptions ListOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    public Task<IReadOnlyList<StorageAnalysisItem>> AnalyzeTopLevelAsync(
        string rootPath,
        StorageAnalysisOptions? options = null,
        IProgress<StorageAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                var sw = Stopwatch.StartNew();
                var counters = new AnalysisCounters();
                var normalizedOptions = StorageAnalysisOptions.Normalize(options);
                return AnalyzeTopLevelCore(rootPath, normalizedOptions, progress, sw, counters, cancellationToken);
            },
            cancellationToken);

    private static IReadOnlyList<StorageAnalysisItem> AnalyzeTopLevelCore(
        string rootPath,
        StorageAnalysisOptions options,
        IProgress<StorageAnalysisProgress>? progress,
        Stopwatch sw,
        AnalysisCounters counters,
        CancellationToken cancellationToken)
    {
        var normalizedRoot = NormalizeAndValidateRoot(rootPath);
        var work = BuildTopLevelWorkList(normalizedRoot);
        var coordinator = new ScanReportCoordinator();

        var startedMessage = options.Mode == StorageAnalysisMode.LimitedParallel
            ? string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"상세 분석 시작: 제한적 병렬 모드, 동시 작업 {options.MaxDegreeOfParallelism}개")
            : "상세 분석 시작: 순차 모드";

        progress?.Report(
            new StorageAnalysisProgress
            {
                Kind = StorageAnalysisProgressKind.Started,
                RootPath = normalizedRoot,
                TotalTopLevelItems = work.Count,
                CompletedTopLevelItems = 0,
                FilesScanned = counters.FilesScanned,
                DirectoriesScanned = counters.DirectoriesScanned,
                BytesScanned = counters.BytesScanned,
                Elapsed = sw.Elapsed,
                Message = startedMessage,
                AnalysisMode = options.Mode,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            });

        return options.Mode == StorageAnalysisMode.LimitedParallel
            ? AnalyzeTopLevelLimitedParallel(
                normalizedRoot,
                work,
                options,
                progress,
                sw,
                counters,
                coordinator,
                cancellationToken)
            : AnalyzeTopLevelSequential(
                normalizedRoot,
                work,
                options,
                progress,
                sw,
                counters,
                coordinator,
                cancellationToken);
    }

    private static IReadOnlyList<StorageAnalysisItem> AnalyzeTopLevelSequential(
        string normalizedRoot,
        List<WorkItem> work,
        StorageAnalysisOptions options,
        IProgress<StorageAnalysisProgress>? progress,
        Stopwatch sw,
        AnalysisCounters counters,
        ScanReportCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var results = new List<StorageAnalysisItem>();
        var completed = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var item in work)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(
                    new StorageAnalysisProgress
                    {
                        Kind = StorageAnalysisProgressKind.TopLevelItemStarted,
                        RootPath = normalizedRoot,
                        CurrentTopLevelName = item.Name,
                        CurrentPath = item.Path,
                        TotalTopLevelItems = work.Count,
                        CompletedTopLevelItems = completed,
                        FilesScanned = counters.FilesScanned,
                        DirectoriesScanned = counters.DirectoriesScanned,
                        BytesScanned = counters.BytesScanned,
                        Elapsed = sw.Elapsed,
                        AnalysisMode = options.Mode,
                        MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                    });

                var itemSw = Stopwatch.StartNew();
                StorageAnalysisItem rawItem;
                try
                {
                    rawItem = item.IsDirectory
                        ? ProcessDirectoryItem(
                            item.Path,
                            item.Name,
                            normalizedRoot,
                            progress,
                            counters,
                            coordinator,
                            completed,
                            work.Count,
                            sw,
                            options,
                            cancellationToken)
                        : ProcessFileItem(item.Path, item.Name, counters, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    rawItem = item.IsDirectory
                        ? new StorageAnalysisItem(
                            item.Name,
                            item.Path,
                            0,
                            StorageEntryType.Directory,
                            0,
                            0,
                            isAccessible: false,
                            "폴더 내용을 읽는 중 오류가 발생해 합산하지 못했습니다. (" + AccessNote + ")")
                        : new StorageAnalysisItem(
                            item.Name,
                            item.Path,
                            0,
                            StorageEntryType.File,
                            0,
                            0,
                            isAccessible: false,
                            "파일을 읽는 중 오류가 발생했습니다. (" + AccessNote + ")");
                }

                var resultItem = WithTopLevelDuration(rawItem, itemSw.Elapsed);
                results.Add(resultItem);
                completed++;

                progress?.Report(
                    new StorageAnalysisProgress
                    {
                        Kind = StorageAnalysisProgressKind.TopLevelItemCompleted,
                        RootPath = normalizedRoot,
                        CurrentTopLevelName = item.Name,
                        CurrentPath = item.Path,
                        TotalTopLevelItems = work.Count,
                        CompletedTopLevelItems = completed,
                        FilesScanned = counters.FilesScanned,
                        DirectoriesScanned = counters.DirectoriesScanned,
                        BytesScanned = counters.BytesScanned,
                        Elapsed = sw.Elapsed,
                        CompletedItem = resultItem,
                        AnalysisMode = options.Mode,
                        MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                    });
            }
        }
        catch (OperationCanceledException ex)
        {
            ReportCancellationAndThrow(
                normalizedRoot,
                work,
                results,
                options,
                progress,
                sw,
                counters,
                cancellationToken,
                ex);
        }

        return FinalizeResults(
            normalizedRoot,
            work,
            results,
            options,
            progress,
            sw,
            counters);
    }

    private static IReadOnlyList<StorageAnalysisItem> AnalyzeTopLevelLimitedParallel(
        string normalizedRoot,
        List<WorkItem> work,
        StorageAnalysisOptions options,
        IProgress<StorageAnalysisProgress>? progress,
        Stopwatch sw,
        AnalysisCounters counters,
        ScanReportCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var dop = Math.Clamp(
            options.MaxDegreeOfParallelism,
            StorageAnalysisOptions.MinParallelism,
            StorageAnalysisOptions.MaxParallelism);

        using var semaphore = new SemaphoreSlim(dop, dop);
        var completion = new ParallelCompletionCounter();
        var tasks = new Task<StorageAnalysisItem>[work.Count];

        for (var i = 0; i < work.Count; i++)
        {
            var capturedItem = work[i];
            tasks[i] = Task.Run(
                () => ProcessTopLevelLimitedParallelOne(
                    capturedItem,
                    normalizedRoot,
                    work.Count,
                    options,
                    progress,
                    sw,
                    counters,
                    coordinator,
                    semaphore,
                    completion,
                    cancellationToken),
                cancellationToken);
        }

        try
        {
            Task.WaitAll(tasks);
        }
        catch (AggregateException ex)
        {
            if (TryExtractOperationCanceled(ex, out var oce))
            {
                var partial = CollectSuccessfulTaskResults(tasks);
                ReportCancellationAndThrow(
                    normalizedRoot,
                    work,
                    partial,
                    options,
                    progress,
                    sw,
                    counters,
                    cancellationToken,
                    oce);
            }

            throw ex.Flatten();
        }
        catch (OperationCanceledException oce)
        {
            var partial = CollectSuccessfulTaskResults(tasks);
            ReportCancellationAndThrow(
                normalizedRoot,
                work,
                partial,
                options,
                progress,
                sw,
                counters,
                cancellationToken,
                oce);
        }

        var results = new List<StorageAnalysisItem>(tasks.Length);
        foreach (var t in tasks)
        {
            results.Add(t.GetAwaiter().GetResult());
        }

        return FinalizeResults(
            normalizedRoot,
            work,
            results,
            options,
            progress,
            sw,
            counters);
    }

    private static bool TryExtractOperationCanceled(AggregateException ex, out OperationCanceledException oce)
    {
        foreach (var inner in ex.InnerExceptions)
        {
            if (inner is OperationCanceledException e)
            {
                oce = e;
                return true;
            }

            if (inner is AggregateException nested && TryExtractOperationCanceled(nested, out oce))
            {
                return true;
            }
        }

        oce = null!;
        return false;
    }

    private static List<StorageAnalysisItem> CollectSuccessfulTaskResults(Task<StorageAnalysisItem>[] tasks)
    {
        var list = new List<StorageAnalysisItem>();
        foreach (var t in tasks)
        {
            if (t.Status == TaskStatus.RanToCompletion)
            {
                list.Add(t.Result);
            }
        }

        return list;
    }

    private static StorageAnalysisItem ProcessTopLevelLimitedParallelOne(
        WorkItem item,
        string normalizedRoot,
        int totalTopLevel,
        StorageAnalysisOptions options,
        IProgress<StorageAnalysisProgress>? progress,
        Stopwatch sw,
        AnalysisCounters counters,
        ScanReportCoordinator coordinator,
        SemaphoreSlim semaphore,
        ParallelCompletionCounter completion,
        CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            semaphore.Wait(cancellationToken);
            acquired = true;

            cancellationToken.ThrowIfCancellationRequested();

            var completedBefore = Volatile.Read(ref completion.CompletedTopLevelItems);

            progress?.Report(
                new StorageAnalysisProgress
                {
                    Kind = StorageAnalysisProgressKind.TopLevelItemStarted,
                    RootPath = normalizedRoot,
                    CurrentTopLevelName = item.Name,
                    CurrentPath = item.Path,
                    TotalTopLevelItems = totalTopLevel,
                    CompletedTopLevelItems = completedBefore,
                    FilesScanned = counters.FilesScanned,
                    DirectoriesScanned = counters.DirectoriesScanned,
                    BytesScanned = counters.BytesScanned,
                    Elapsed = sw.Elapsed,
                    AnalysisMode = options.Mode,
                    MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                });

            var itemSw = Stopwatch.StartNew();
            StorageAnalysisItem rawItem;
            try
            {
                rawItem = item.IsDirectory
                    ? ProcessDirectoryItem(
                        item.Path,
                        item.Name,
                        normalizedRoot,
                        progress,
                        counters,
                        coordinator,
                        completedBefore,
                        totalTopLevel,
                        sw,
                        options,
                        cancellationToken)
                    : ProcessFileItem(item.Path, item.Name, counters, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                rawItem = item.IsDirectory
                    ? new StorageAnalysisItem(
                        item.Name,
                        item.Path,
                        0,
                        StorageEntryType.Directory,
                        0,
                        0,
                        isAccessible: false,
                        "폴더 내용을 읽는 중 오류가 발생해 합산하지 못했습니다. (" + AccessNote + ")")
                    : new StorageAnalysisItem(
                        item.Name,
                        item.Path,
                        0,
                        StorageEntryType.File,
                        0,
                        0,
                        isAccessible: false,
                        "파일을 읽는 중 오류가 발생했습니다. (" + AccessNote + ")");
            }

            var resultItem = WithTopLevelDuration(rawItem, itemSw.Elapsed);
            var done = Interlocked.Increment(ref completion.CompletedTopLevelItems);

            progress?.Report(
                new StorageAnalysisProgress
                {
                    Kind = StorageAnalysisProgressKind.TopLevelItemCompleted,
                    RootPath = normalizedRoot,
                    CurrentTopLevelName = item.Name,
                    CurrentPath = item.Path,
                    TotalTopLevelItems = totalTopLevel,
                    CompletedTopLevelItems = done,
                    FilesScanned = counters.FilesScanned,
                    DirectoriesScanned = counters.DirectoriesScanned,
                    BytesScanned = counters.BytesScanned,
                    Elapsed = sw.Elapsed,
                    CompletedItem = resultItem,
                    AnalysisMode = options.Mode,
                    MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                });

            return resultItem;
        }
        finally
        {
            if (acquired)
            {
                semaphore.Release();
            }
        }
    }

    private sealed class ParallelCompletionCounter
    {
        public int CompletedTopLevelItems;
    }

    private static void ReportCancellationAndThrow(
        string normalizedRoot,
        List<WorkItem> work,
        List<StorageAnalysisItem> partialResults,
        StorageAnalysisOptions options,
        IProgress<StorageAnalysisProgress>? progress,
        Stopwatch sw,
        AnalysisCounters counters,
        CancellationToken cancellationToken,
        OperationCanceledException? existing = null)
    {
        partialResults.Sort(static (a, b) =>
        {
            var cmp = b.SizeBytes.CompareTo(a.SizeBytes);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        var partialSummary = StorageAnalysisPerformanceSummary.Create(
            partialResults,
            counters.FilesScanned,
            counters.DirectoriesScanned,
            counters.BytesScanned,
            sw.Elapsed,
            plannedTopLevelCount: work.Count,
            analysisMode: options.Mode,
            maxDegreeOfParallelism: options.MaxDegreeOfParallelism);

        progress?.Report(
            new StorageAnalysisProgress
            {
                Kind = StorageAnalysisProgressKind.Cancelled,
                RootPath = normalizedRoot,
                TotalTopLevelItems = work.Count,
                CompletedTopLevelItems = partialResults.Count,
                FilesScanned = counters.FilesScanned,
                DirectoriesScanned = counters.DirectoriesScanned,
                BytesScanned = counters.BytesScanned,
                Elapsed = sw.Elapsed,
                Message = "분석이 취소되었습니다.",
                PerformanceSummary = partialSummary,
                AnalysisMode = options.Mode,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            });

        throw existing ?? new OperationCanceledException(cancellationToken);
    }

    private static IReadOnlyList<StorageAnalysisItem> FinalizeResults(
        string normalizedRoot,
        List<WorkItem> work,
        List<StorageAnalysisItem> results,
        StorageAnalysisOptions options,
        IProgress<StorageAnalysisProgress>? progress,
        Stopwatch sw,
        AnalysisCounters counters)
    {
        results.Sort(static (a, b) =>
        {
            var cmp = b.SizeBytes.CompareTo(a.SizeBytes);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        var completed = results.Count;

        var completedSummary = StorageAnalysisPerformanceSummary.Create(
            results,
            counters.FilesScanned,
            counters.DirectoriesScanned,
            counters.BytesScanned,
            sw.Elapsed,
            plannedTopLevelCount: work.Count,
            analysisMode: options.Mode,
            maxDegreeOfParallelism: options.MaxDegreeOfParallelism);

        progress?.Report(
            new StorageAnalysisProgress
            {
                Kind = StorageAnalysisProgressKind.Completed,
                RootPath = normalizedRoot,
                TotalTopLevelItems = work.Count,
                CompletedTopLevelItems = completed,
                FilesScanned = counters.FilesScanned,
                DirectoriesScanned = counters.DirectoriesScanned,
                BytesScanned = counters.BytesScanned,
                Elapsed = sw.Elapsed,
                Message = options.Mode == StorageAnalysisMode.LimitedParallel
                    ? "상세 분석 완료: 제한적 병렬 모드"
                    : "상세 분석 완료: 순차 모드",
                PerformanceSummary = completedSummary,
                AnalysisMode = options.Mode,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            });

        return results;
    }

    private static StorageAnalysisItem WithTopLevelDuration(StorageAnalysisItem item, TimeSpan duration) =>
        new StorageAnalysisItem(
            item.Name,
            item.Path,
            item.SizeBytes,
            item.EntryType,
            item.FileCount,
            item.DirectoryCount,
            item.IsAccessible,
            item.Note,
            duration);

    private sealed record WorkItem(string Path, bool IsDirectory, string Name);

    private sealed class AnalysisCounters
    {
        private long _filesScanned;
        private long _directoriesScanned;
        private long _bytesScanned;

        public long FilesScanned => Interlocked.Read(ref _filesScanned);

        public long DirectoriesScanned => Interlocked.Read(ref _directoriesScanned);

        public long BytesScanned => Interlocked.Read(ref _bytesScanned);

        public void AddSuccessfulFile(long length)
        {
            Interlocked.Increment(ref _filesScanned);
            Interlocked.Add(ref _bytesScanned, length);
        }

        public void AddDirectoryVisit() => Interlocked.Increment(ref _directoriesScanned);
    }

    private sealed class ScanReportCoordinator
    {
        private long _filesAtLastReport;
        private long _lastReportTicks;
        private readonly object _lock = new();

        public void TryReportScanning(
            IProgress<StorageAnalysisProgress>? progress,
            string rootPath,
            string topLevelName,
            int completedTopLevelBeforeThis,
            int totalTopLevel,
            string currentPath,
            AnalysisCounters counters,
            Stopwatch sw,
            StorageAnalysisOptions options)
        {
            if (progress is null)
            {
                return;
            }

            lock (_lock)
            {
                var now = Stopwatch.GetTimestamp();
                var totalFiles = counters.FilesScanned;
                var filesDelta = totalFiles - _filesAtLastReport;
                var elapsedMs = (now - _lastReportTicks) * 1000.0 / Stopwatch.Frequency;
                if (filesDelta < MinFilesBetweenScanReports && elapsedMs < MinMillisecondsBetweenScanReports)
                {
                    return;
                }

                _filesAtLastReport = totalFiles;
                _lastReportTicks = now;

                progress.Report(
                    new StorageAnalysisProgress
                    {
                        Kind = StorageAnalysisProgressKind.Scanning,
                        RootPath = rootPath,
                        CurrentTopLevelName = topLevelName,
                        CurrentPath = currentPath,
                        TotalTopLevelItems = totalTopLevel,
                        CompletedTopLevelItems = completedTopLevelBeforeThis,
                        FilesScanned = counters.FilesScanned,
                        DirectoriesScanned = counters.DirectoriesScanned,
                        BytesScanned = counters.BytesScanned,
                        Elapsed = sw.Elapsed,
                        AnalysisMode = options.Mode,
                        MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                    });
            }
        }
    }

    private static List<WorkItem> BuildTopLevelWorkList(string normalizedRoot)
    {
        var list = new List<WorkItem>();

        foreach (var directoryPath in EnumerateDirectoriesSafe(normalizedRoot))
        {
            list.Add(new WorkItem(directoryPath, true, GetTopLevelName(directoryPath)));
        }

        foreach (var filePath in EnumerateFilesSafe(normalizedRoot))
        {
            var name = Path.GetFileName(filePath);
            if (!string.IsNullOrEmpty(name))
            {
                list.Add(new WorkItem(filePath, false, name));
            }
        }

        return list;
    }

    private static StorageAnalysisItem ProcessDirectoryItem(
        string directoryPath,
        string name,
        string normalizedRoot,
        IProgress<StorageAnalysisProgress>? progress,
        AnalysisCounters counters,
        ScanReportCoordinator coordinator,
        int completedTopLevelBeforeThis,
        int totalTopLevel,
        Stopwatch sw,
        StorageAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        if (IsDirectoryReparsePoint(directoryPath))
        {
            return new StorageAnalysisItem(
                name,
                directoryPath,
                0,
                StorageEntryType.Directory,
                0,
                0,
                isAccessible: true,
                ReparseNote);
        }

        var (size, files, dirs) = ScanDirectoryContents(
            directoryPath,
            normalizedRoot,
            name,
            progress,
            counters,
            coordinator,
            completedTopLevelBeforeThis,
            totalTopLevel,
            sw,
            options,
            cancellationToken);

        return new StorageAnalysisItem(
            name,
            directoryPath,
            size,
            StorageEntryType.Directory,
            files,
            dirs,
            isAccessible: true,
            note: null);
    }

    private static StorageAnalysisItem ProcessFileItem(
        string filePath,
        string name,
        AnalysisCounters counters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (TryGetFileMetadata(filePath, out _, out var length))
        {
            case FileMetadataOutcome.ReparsePoint:
                return new StorageAnalysisItem(
                    name,
                    filePath,
                    0,
                    StorageEntryType.File,
                    0,
                    0,
                    isAccessible: true,
                    ReparseNote);

            case FileMetadataOutcome.Success:
                counters.AddSuccessfulFile(length);

                return new StorageAnalysisItem(
                    name,
                    filePath,
                    length,
                    StorageEntryType.File,
                    fileCount: 1,
                    directoryCount: 0,
                    isAccessible: true,
                    note: null);

            case FileMetadataOutcome.Unavailable:
            default:
                return new StorageAnalysisItem(
                    name,
                    filePath,
                    0,
                    StorageEntryType.File,
                    0,
                    0,
                    isAccessible: false,
                    "파일 크기를 읽지 못했습니다. (" + AccessNote + ")");
        }
    }

    private static (long Size, int Files, int Directories) ScanDirectoryContents(
        string rootDir,
        string rootPath,
        string topLevelName,
        IProgress<StorageAnalysisProgress>? progress,
        AnalysisCounters counters,
        ScanReportCoordinator coordinator,
        int completedTopLevelBeforeThis,
        int totalTopLevel,
        Stopwatch sw,
        StorageAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        long size = 0;
        var files = 0;
        var dirs = 0;

        var stack = new Stack<string>();
        stack.Push(rootDir);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = stack.Pop();
            counters.AddDirectoryVisit();

            foreach (var file in EnumerateFilesSafe(current))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryGetFileMetadata(file, out _, out var len) != FileMetadataOutcome.Success)
                {
                    continue;
                }

                size += len;
                files++;
                counters.AddSuccessfulFile(len);

                coordinator.TryReportScanning(
                    progress,
                    rootPath,
                    topLevelName,
                    completedTopLevelBeforeThis,
                    totalTopLevel,
                    file,
                    counters,
                    sw,
                    options);
            }

            foreach (var dir in EnumerateDirectoriesSafe(current))
            {
                cancellationToken.ThrowIfCancellationRequested();

                dirs++;

                if (IsDirectoryReparsePoint(dir))
                {
                    continue;
                }

                stack.Push(dir);
            }
        }

        return (size, files, dirs);
    }

    private static string NormalizeAndValidateRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        var trimmed = rootPath.Trim();

        if (trimmed.Length == 2 && trimmed[1] == ':')
        {
            trimmed = string.Concat(trimmed, Path.DirectorySeparatorChar);
        }
        else
        {
            trimmed = Path.GetFullPath(trimmed);
        }

        if (!Directory.Exists(trimmed))
        {
            throw new DirectoryNotFoundException($"Directory not found: {trimmed}");
        }

        if (!trimmed.EndsWith(Path.DirectorySeparatorChar) && !trimmed.EndsWith(Path.AltDirectorySeparatorChar))
        {
            trimmed += Path.DirectorySeparatorChar;
        }

        return trimmed;
    }

    private static string GetTopLevelName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed);
    }

    private static bool IsDirectoryReparsePoint(string directoryPath)
    {
        try
        {
            return (new DirectoryInfo(directoryPath).Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (PathTooLongException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (SecurityException)
        {
            return true;
        }
    }

    private enum FileMetadataOutcome
    {
        Success,
        ReparsePoint,
        Unavailable,
    }

    private static FileMetadataOutcome TryGetFileMetadata(string filePath, out bool isReparsePoint, out long length)
    {
        isReparsePoint = false;
        length = 0;

        try
        {
            var info = new FileInfo(filePath);
            var attributes = info.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                isReparsePoint = true;
                return FileMetadataOutcome.ReparsePoint;
            }

            length = info.Length;
            return FileMetadataOutcome.Success;
        }
        catch (FileNotFoundException)
        {
            return FileMetadataOutcome.Unavailable;
        }
        catch (IOException)
        {
            return FileMetadataOutcome.Unavailable;
        }
        catch (UnauthorizedAccessException)
        {
            return FileMetadataOutcome.Unavailable;
        }
        catch (SecurityException)
        {
            return FileMetadataOutcome.Unavailable;
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string directoryPath)
    {
        try
        {
            return Directory.EnumerateFiles(directoryPath, "*", ListOptions);
        }
        catch (PathTooLongException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (SecurityException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string directoryPath)
    {
        try
        {
            return Directory.EnumerateDirectories(directoryPath, "*", ListOptions);
        }
        catch (PathTooLongException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (SecurityException)
        {
            return Array.Empty<string>();
        }
    }
}
