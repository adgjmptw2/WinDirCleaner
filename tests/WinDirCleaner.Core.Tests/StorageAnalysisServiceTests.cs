using System.Diagnostics;
using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Services;

namespace WinDirCleaner.Core.Tests;

public sealed class StorageAnalysisServiceTests
{
    [Fact]
    public async Task IncludesTopLevelFileSizes()
    {
        var root = CreateTempRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), new string('a', 7));
            await File.WriteAllTextAsync(Path.Combine(root, "b.txt"), new string('b', 3));

            var service = new StorageAnalysisService();
            var result = await service.AnalyzeTopLevelAsync(root);

            var a = result.Single(x => x.Name == "a.txt");
            var b = result.Single(x => x.Name == "b.txt");
            Assert.Equal(7, a.SizeBytes);
            Assert.Equal(3, b.SizeBytes);
            Assert.Equal(StorageEntryType.File, a.EntryType);
            Assert.Equal(1, a.FileCount);
            Assert.Equal(0, a.DirectoryCount);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task SumsNestedFilesInsideTopLevelDirectory()
    {
        var root = CreateTempRoot();
        try
        {
            var dir = Path.Combine(root, "nested");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "inner.txt"), new string('z', 11));

            var service = new StorageAnalysisService();
            var result = await service.AnalyzeTopLevelAsync(root);

            var item = result.Single(x => x.Name == "nested");
            Assert.Equal(StorageEntryType.Directory, item.EntryType);
            Assert.Equal(11, item.SizeBytes);
            Assert.Equal(1, item.FileCount);
            Assert.True(item.DirectoryCount >= 0);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ResultsAreSortedBySizeDescendingThenName()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bigdir"));
            await File.WriteAllTextAsync(Path.Combine(root, "bigdir", "x.bin"), new string('x', 20));

            await File.WriteAllTextAsync(Path.Combine(root, "small.txt"), new string('s', 5));

            await File.WriteAllTextAsync(Path.Combine(root, "mid.txt"), new string('m', 10));

            var service = new StorageAnalysisService();
            var result = (await service.AnalyzeTopLevelAsync(root)).ToList();

            Assert.Equal("bigdir", result[0].Name);
            Assert.Equal("mid.txt", result[1].Name);
            Assert.Equal("small.txt", result[2].Name);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task EmptyDirectory_IsIncludedWithZeroSize()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "empty"));

            var service = new StorageAnalysisService();
            var result = await service.AnalyzeTopLevelAsync(root);

            var empty = result.Single(x => x.Name == "empty");
            Assert.Equal(0, empty.SizeBytes);
            Assert.Equal(StorageEntryType.Directory, empty.EntryType);
            Assert.True(empty.IsAccessible);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task AnalyzeTopLevelAsync_SubfolderAsRoot_ReturnsImmediateChildrenOnly()
    {
        var root = CreateTempRoot();
        try
        {
            var focus = Path.Combine(root, "focus");
            Directory.CreateDirectory(focus);
            Directory.CreateDirectory(Path.Combine(focus, "childdir"));
            await File.WriteAllTextAsync(Path.Combine(focus, "rootfile.txt"), "ab");
            await File.WriteAllTextAsync(Path.Combine(focus, "childdir", "inner.txt"), "z");

            var service = new StorageAnalysisService();
            var result = await service.AnalyzeTopLevelAsync(focus);

            var names = result.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("rootfile.txt", names);
            Assert.Contains("childdir", names);
            Assert.DoesNotContain("inner.txt", names);
            var dir = result.Single(r => r.Name == "childdir");
            Assert.Equal(StorageEntryType.Directory, dir.EntryType);
            Assert.Equal(1, dir.FileCount);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MissingPath_ThrowsDirectoryNotFound()
    {
        var root = CreateTempRoot();
        try
        {
            var missing = Path.Combine(root, "does-not-exist");
            var service = new StorageAnalysisService();

            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.AnalyzeTopLevelAsync(missing));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task PreCanceledToken_ThrowsOperationCanceled()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new StorageAnalysisService();
            var token = new CancellationToken(true);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeTopLevelAsync(root, cancellationToken: token));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Progress_ReportsStartedAndCompleted()
    {
        var root = CreateTempRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "x");

            var kinds = new List<StorageAnalysisProgressKind>();
            var progress = new SynchronousStorageAnalysisProgress(p => kinds.Add(p.Kind));

            var service = new StorageAnalysisService();
            await service.AnalyzeTopLevelAsync(root, progress: progress, cancellationToken: CancellationToken.None);

            Assert.Contains(StorageAnalysisProgressKind.Started, kinds);
            Assert.Contains(StorageAnalysisProgressKind.Completed, kinds);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Progress_TopLevelItemCompletedIncludesItem()
    {
        var root = CreateTempRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "x");
            await File.WriteAllTextAsync(Path.Combine(root, "b.txt"), "yy");

            var completed = new List<StorageAnalysisItem>();
            var progress = new SynchronousStorageAnalysisProgress(p =>
            {
                if (p.Kind == StorageAnalysisProgressKind.TopLevelItemCompleted && p.CompletedItem is not null)
                {
                    completed.Add(p.CompletedItem);
                }
            });

            var service = new StorageAnalysisService();
            await service.AnalyzeTopLevelAsync(root, progress: progress, cancellationToken: CancellationToken.None);

            Assert.Equal(2, completed.Count);
            Assert.All(completed, i => Assert.False(string.IsNullOrWhiteSpace(i.Name)));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Progress_StartedAppearsBeforeCompleted()
    {
        var root = CreateTempRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "only.txt"), "z");

            var kinds = new List<StorageAnalysisProgressKind>();
            var progress = new SynchronousStorageAnalysisProgress(p => kinds.Add(p.Kind));

            var service = new StorageAnalysisService();
            await service.AnalyzeTopLevelAsync(root, progress: progress, cancellationToken: CancellationToken.None);

            var started = kinds.IndexOf(StorageAnalysisProgressKind.Started);
            var completed = kinds.IndexOf(StorageAnalysisProgressKind.Completed);
            Assert.True(started >= 0);
            Assert.True(completed > started);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Progress_TopLevelItemCompletedCountMatchesItems()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "d1"));
            await File.WriteAllTextAsync(Path.Combine(root, "d1", "a.txt"), "x");
            Directory.CreateDirectory(Path.Combine(root, "d2"));
            await File.WriteAllTextAsync(Path.Combine(root, "f.txt"), "yy");

            var completedEvents = 0;
            var progress = new SynchronousStorageAnalysisProgress(p =>
            {
                if (p.Kind == StorageAnalysisProgressKind.TopLevelItemCompleted)
                {
                    completedEvents++;
                }
            });

            var service = new StorageAnalysisService();
            var result = await service.AnalyzeTopLevelAsync(root, progress: progress, cancellationToken: CancellationToken.None);

            Assert.Equal(result.Count, completedEvents);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Cancelled_ProgressIncludesScanCountersWhenCancelledAfterFirstTopLevel()
    {
        var root = CreateTempRoot();
        try
        {
            var d1 = Path.Combine(root, "folder_a");
            Directory.CreateDirectory(d1);
            await File.WriteAllTextAsync(Path.Combine(d1, "f1.txt"), "abc");
            var d2 = Path.Combine(root, "folder_b");
            Directory.CreateDirectory(d2);
            await File.WriteAllTextAsync(Path.Combine(d2, "f2.txt"), "defghi");

            var cts = new CancellationTokenSource();
            StorageAnalysisProgress? lastCancelled = null;
            var progress = new SynchronousStorageAnalysisProgress(p =>
            {
                if (p.Kind == StorageAnalysisProgressKind.TopLevelItemCompleted && p.CompletedTopLevelItems == 1)
                {
                    cts.Cancel();
                }

                if (p.Kind == StorageAnalysisProgressKind.Cancelled)
                {
                    lastCancelled = p;
                }
            });

            var service = new StorageAnalysisService();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.AnalyzeTopLevelAsync(root, progress: progress, cancellationToken: cts.Token));

            Assert.NotNull(lastCancelled);
            Assert.True(lastCancelled!.FilesScanned >= 1);
            Assert.NotNull(lastCancelled.PerformanceSummary);
            Assert.True(lastCancelled.PerformanceSummary!.TotalElapsed >= TimeSpan.Zero);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task TopLevelItemCompleted_HasNonNegativeAnalysisDuration()
    {
        var root = CreateTempRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "x");

            StorageAnalysisItem? last = null;
            var progress = new SynchronousStorageAnalysisProgress(p =>
            {
                if (p.Kind == StorageAnalysisProgressKind.TopLevelItemCompleted && p.CompletedItem is not null)
                {
                    last = p.CompletedItem;
                }
            });

            var service = new StorageAnalysisService();
            await service.AnalyzeTopLevelAsync(root, progress: progress, cancellationToken: CancellationToken.None);

            Assert.NotNull(last);
            Assert.True(last!.TopLevelAnalysisDuration >= TimeSpan.Zero);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task CompletedProgress_HasPerformanceSummary()
    {
        var root = CreateTempRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "x");

            StorageAnalysisProgress? completed = null;
            var progress = new SynchronousStorageAnalysisProgress(p =>
            {
                if (p.Kind == StorageAnalysisProgressKind.Completed)
                {
                    completed = p;
                }
            });

            var service = new StorageAnalysisService();
            await service.AnalyzeTopLevelAsync(root, progress: progress, cancellationToken: CancellationToken.None);

            Assert.NotNull(completed?.PerformanceSummary);
            Assert.True(completed!.PerformanceSummary!.TotalElapsed >= TimeSpan.Zero);
            Assert.Equal(1, completed.PerformanceSummary.CompletedTopLevelItemCount);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task AnalysisSizesMatchWithOrWithoutProgressHandler()
    {
        var root = CreateTempRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), new string('z', 5));

            var service = new StorageAnalysisService();
            var withProgress = await service.AnalyzeTopLevelAsync(
                root,
                progress: new SynchronousStorageAnalysisProgress(_ => { }),
                cancellationToken: CancellationToken.None);
            var plain = await service.AnalyzeTopLevelAsync(root, cancellationToken: CancellationToken.None);

            Assert.Equal(plain.Single().SizeBytes, withProgress.Single().SizeBytes);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void StorageAnalysisOptions_Default_IsSequentialWithMaxParallelismTwo()
    {
        var d = StorageAnalysisOptions.Default;
        Assert.Equal(StorageAnalysisMode.Sequential, d.Mode);
        Assert.Equal(2, d.MaxDegreeOfParallelism);
    }

    [Fact]
    public void StorageAnalysisOptions_Normalize_ClampsMaxDegreeOfParallelismToFour()
    {
        var raw = new StorageAnalysisOptions
        {
            Mode = StorageAnalysisMode.LimitedParallel,
            MaxDegreeOfParallelism = 99,
        };

        var n = StorageAnalysisOptions.Normalize(raw);
        Assert.Equal(StorageAnalysisMode.LimitedParallel, n.Mode);
        Assert.Equal(4, n.MaxDegreeOfParallelism);
    }

    [Fact]
    public async Task SequentialAndLimitedParallel_ReturnSameTotalSizeBytes_OnTempTree()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "d1"));
            await File.WriteAllTextAsync(Path.Combine(root, "d1", "a.txt"), new string('a', 4));
            Directory.CreateDirectory(Path.Combine(root, "d2"));
            await File.WriteAllTextAsync(Path.Combine(root, "d2", "b.txt"), new string('b', 6));
            await File.WriteAllTextAsync(Path.Combine(root, "root.txt"), "zz");

            var service = new StorageAnalysisService();
            var seq = await service.AnalyzeTopLevelAsync(
                root,
                new StorageAnalysisOptions { Mode = StorageAnalysisMode.Sequential, MaxDegreeOfParallelism = 1 });
            var par = await service.AnalyzeTopLevelAsync(
                root,
                new StorageAnalysisOptions { Mode = StorageAnalysisMode.LimitedParallel, MaxDegreeOfParallelism = 2 });

            Assert.Equal(seq.Sum(x => x.SizeBytes), par.Sum(x => x.SizeBytes));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LimitedParallel_ResultsSortedBySizeDescendingThenName()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "big"));
            await File.WriteAllTextAsync(Path.Combine(root, "big", "x.bin"), new string('x', 20));
            await File.WriteAllTextAsync(Path.Combine(root, "small.txt"), "s");

            var service = new StorageAnalysisService();
            var result = (await service.AnalyzeTopLevelAsync(
                root,
                new StorageAnalysisOptions { Mode = StorageAnalysisMode.LimitedParallel, MaxDegreeOfParallelism = 2 })).ToList();

            Assert.Equal("big", result[0].Name);
            Assert.Equal("small.txt", result[1].Name);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LimitedParallel_TopLevelItemCompletedCountMatchesResultCount()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a"));
            await File.WriteAllTextAsync(Path.Combine(root, "a", "f.txt"), "x");
            await File.WriteAllTextAsync(Path.Combine(root, "b.txt"), "yy");

            var completed = 0;
            var progress = new SynchronousStorageAnalysisProgress(p =>
            {
                if (p.Kind == StorageAnalysisProgressKind.TopLevelItemCompleted)
                {
                    completed++;
                }
            });

            var service = new StorageAnalysisService();
            var result = await service.AnalyzeTopLevelAsync(
                root,
                new StorageAnalysisOptions { Mode = StorageAnalysisMode.LimitedParallel, MaxDegreeOfParallelism = 2 },
                progress: progress,
                cancellationToken: CancellationToken.None);

            Assert.Equal(result.Count, completed);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LimitedParallel_RespectsCancellationToken()
    {
        var root = CreateTempRoot();
        try
        {
            var d1 = Path.Combine(root, "folder_a");
            Directory.CreateDirectory(d1);
            for (var i = 0; i < 40; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(d1, $"s{i}.txt"), new string('a', 80));
            }

            var d2 = Path.Combine(root, "folder_b");
            Directory.CreateDirectory(d2);
            await File.WriteAllTextAsync(Path.Combine(d2, "f2.txt"), "defghi");

            var cts = new CancellationTokenSource();
            StorageAnalysisProgress? lastCancelled = null;
            var progress = new SynchronousStorageAnalysisProgress(p =>
            {
                if (p.Kind == StorageAnalysisProgressKind.TopLevelItemCompleted && p.CompletedTopLevelItems == 1)
                {
                    cts.Cancel();
                }

                if (p.Kind == StorageAnalysisProgressKind.Cancelled)
                {
                    lastCancelled = p;
                }
            });

            var service = new StorageAnalysisService();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.AnalyzeTopLevelAsync(
                    root,
                    new StorageAnalysisOptions { Mode = StorageAnalysisMode.LimitedParallel, MaxDegreeOfParallelism = 2 },
                    progress: progress,
                    cancellationToken: cts.Token));

            Assert.NotNull(lastCancelled);
            Assert.NotNull(lastCancelled!.PerformanceSummary);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LimitedParallel_JunctionTopLevelMatchesSequential_WhenMklinkSucceeds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempRoot();
        try
        {
            var target = Path.Combine(root, "realdir");
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "inner.txt"), new string('x', 25));
            var junction = Path.Combine(root, "jdlink");

            using var p = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c mklink /J \"" + junction + "\" \"" + target + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                });
            Assert.NotNull(p);
            p.WaitForExit(8000);

            var service = new StorageAnalysisService();
            if (p.ExitCode != 0 || !Directory.Exists(junction))
            {
                var fallback = await service.AnalyzeTopLevelAsync(
                    root,
                    new StorageAnalysisOptions { Mode = StorageAnalysisMode.LimitedParallel, MaxDegreeOfParallelism = 2 });
                Assert.Contains(fallback, x => x.Name == "realdir");
                return;
            }

            var seq = await service.AnalyzeTopLevelAsync(
                root,
                new StorageAnalysisOptions { Mode = StorageAnalysisMode.Sequential, MaxDegreeOfParallelism = 1 });
            var par = await service.AnalyzeTopLevelAsync(
                root,
                new StorageAnalysisOptions { Mode = StorageAnalysisMode.LimitedParallel, MaxDegreeOfParallelism = 2 });

            Assert.Equal(seq.Sum(x => x.SizeBytes), par.Sum(x => x.SizeBytes));
            var jSeq = seq.Single(x => x.Name == "jdlink");
            var jPar = par.Single(x => x.Name == "jdlink");
            Assert.True((jSeq.Note ?? string.Empty).Contains(ReparseNoteKeyword(), StringComparison.Ordinal));
            Assert.True((jPar.Note ?? string.Empty).Contains(ReparseNoteKeyword(), StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string ReparseNoteKeyword() => "재분석 지점";

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "WinDirCleanerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

internal sealed class SynchronousStorageAnalysisProgress : IProgress<StorageAnalysisProgress>
{
    private readonly Action<StorageAnalysisProgress> _onReport;

    public SynchronousStorageAnalysisProgress(Action<StorageAnalysisProgress> onReport) =>
        _onReport = onReport;

    public void Report(StorageAnalysisProgress value) => _onReport(value);
}
