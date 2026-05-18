using System.Diagnostics;
using System.Security;
using WinDirCleaner.Core.Models;

namespace WinDirCleaner.Core.Services;

public sealed class CleanupPreviewService : ICleanupPreviewService
{
    internal const int MaxSampleTargets = 50;

    private const int CancellationCheckInterval = 64;

    internal static bool IsDryRunEligible(CleanupItem candidate) =>
        candidate.Selected
        && candidate.Risk is CleanupRisk.Recommended or CleanupRisk.Optional
        && candidate.Selectable
        && candidate.CanDelete
        && !string.IsNullOrWhiteSpace(candidate.Path);

    public Task<CleanupPreviewResult> PreviewAsync(
        IReadOnlyList<CleanupItem> candidates,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => PreviewCore(candidates, cancellationToken), cancellationToken);

    private static CleanupPreviewResult PreviewCore(
        IReadOnlyList<CleanupItem> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messages = new List<string>();
        var sampleTargets = new List<CleanupPreviewTarget>();

        var selectedCandidateCount = candidates.Count(c => c.Selected);
        var scannedCandidateCount = 0;
        var skippedCandidateCount = 0;
        var targetFileCount = 0;
        var targetDirectoryCount = 0;
        var inaccessibleCount = 0;
        var failedCount = 0;
        long estimatedBytes = 0;

        messages.Add("재분석 지점(정션·심볼릭 링크 등)은 따라가지 않습니다.");

        var sw = Stopwatch.StartNew();

        foreach (var c in candidates)
        {
            if (!c.Selected)
            {
                continue;
            }

            if (!IsDryRunEligible(c))
            {
                skippedCandidateCount++;
                continue;
            }

            if (!TryResolvePath(c.Path, out var full))
            {
                skippedCandidateCount++;
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(full) && !Directory.Exists(full))
            {
                skippedCandidateCount++;
                continue;
            }

            try
            {
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                {
                    skippedCandidateCount++;
                    continue;
                }
            }
            catch
            {
                skippedCandidateCount++;
                continue;
            }

            scannedCandidateCount++;

            if (File.Exists(full))
            {
                CountSingleFile(
                    full,
                    c,
                    sampleTargets,
                    ref targetFileCount,
                    ref estimatedBytes,
                    ref inaccessibleCount,
                    ref failedCount,
                    cancellationToken);
            }
            else
            {
                WalkDirectoryTree(
                    full,
                    c,
                    sampleTargets,
                    ref targetFileCount,
                    ref targetDirectoryCount,
                    ref estimatedBytes,
                    ref inaccessibleCount,
                    ref failedCount,
                    cancellationToken);
            }
        }

        sw.Stop();

        if (inaccessibleCount > 0)
        {
            messages.Add("접근이 제한된 항목은 건너뛰었습니다.");
        }

        var summary = new CleanupPreviewSummary(
            selectedCandidateCount,
            scannedCandidateCount,
            skippedCandidateCount,
            targetFileCount,
            targetDirectoryCount,
            inaccessibleCount,
            failedCount,
            estimatedBytes,
            sw.Elapsed);

        return new CleanupPreviewResult(summary, sampleTargets, messages);
    }

    private static bool TryResolvePath(string path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string expanded;
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        }
        catch
        {
            return false;
        }

        try
        {
            full = Path.GetFullPath(expanded);
        }
        catch
        {
            return false;
        }

        return true;
    }

    private static void CountSingleFile(
        string fullPath,
        CleanupItem source,
        List<CleanupPreviewTarget> sampleTargets,
        ref int targetFileCount,
        ref long estimatedBytes,
        ref int inaccessibleCount,
        ref int failedCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long len;
        try
        {
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            len = new FileInfo(fullPath).Length;
            if (len < 0)
            {
                len = 0;
            }
        }
        catch (UnauthorizedAccessException)
        {
            inaccessibleCount++;
            return;
        }
        catch (SecurityException)
        {
            inaccessibleCount++;
            return;
        }
        catch (PathTooLongException)
        {
            failedCount++;
            return;
        }
        catch (IOException)
        {
            failedCount++;
            return;
        }

        targetFileCount++;
        try
        {
            estimatedBytes = checked(estimatedBytes + len);
        }
        catch (OverflowException)
        {
            estimatedBytes = long.MaxValue;
        }

        TryAddSample(
            sampleTargets,
            Path.GetFileName(fullPath),
            fullPath,
            len,
            isDirectory: false,
            source);
    }

    private static void WalkDirectoryTree(
        string rootDirectory,
        CleanupItem source,
        List<CleanupPreviewTarget> sampleTargets,
        ref int targetFileCount,
        ref int targetDirectoryCount,
        ref long estimatedBytes,
        ref int inaccessibleCount,
        ref int failedCount,
        CancellationToken cancellationToken)
    {
        var enumOptions = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        var stack = new Stack<string>();
        stack.Push(rootDirectory);
        var opCount = 0;

        while (stack.Count > 0)
        {
            if (++opCount % CancellationCheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var dir = stack.Pop();

            try
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            targetDirectoryCount++;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", enumOptions))
                {
                    if (++opCount % CancellationCheckInterval == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    try
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        long len;
                        try
                        {
                            len = new FileInfo(file).Length;
                        }
                        catch (FileNotFoundException)
                        {
                            continue;
                        }

                        if (len < 0)
                        {
                            len = 0;
                        }

                        targetFileCount++;
                        try
                        {
                            estimatedBytes = checked(estimatedBytes + len);
                        }
                        catch (OverflowException)
                        {
                            estimatedBytes = long.MaxValue;
                        }

                        TryAddSample(
                            sampleTargets,
                            Path.GetFileName(file),
                            file,
                            len,
                            isDirectory: false,
                            source);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        inaccessibleCount++;
                    }
                    catch (SecurityException)
                    {
                        inaccessibleCount++;
                    }
                    catch (PathTooLongException)
                    {
                        failedCount++;
                    }
                    catch (IOException)
                    {
                        failedCount++;
                    }
                }

                foreach (var sub in Directory.EnumerateDirectories(dir, "*", enumOptions))
                {
                    if (++opCount % CancellationCheckInterval == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    try
                    {
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        stack.Push(sub);
                    }
                    catch
                    {
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                inaccessibleCount++;
            }
            catch (SecurityException)
            {
                inaccessibleCount++;
            }
            catch (PathTooLongException)
            {
                failedCount++;
            }
            catch (DirectoryNotFoundException)
            {
                failedCount++;
            }
            catch (IOException)
            {
                failedCount++;
            }
        }
    }

    private static void TryAddSample(
        List<CleanupPreviewTarget> sampleTargets,
        string name,
        string path,
        long sizeBytes,
        bool isDirectory,
        CleanupItem source)
    {
        if (sampleTargets.Count >= MaxSampleTargets)
        {
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(name) ? path : name;
        sampleTargets.Add(
            new CleanupPreviewTarget(
                displayName,
                path,
                sizeBytes,
                isDirectory,
                source.Id,
                source.Name));
    }
}
