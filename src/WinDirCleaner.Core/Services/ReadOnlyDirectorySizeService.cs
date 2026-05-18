using System.Security;

namespace WinDirCleaner.Core.Services;

/// <summary>
/// 재분석 지점·접근 불가 항목은 건너뜁니다. 심볼릭 링크/정션 등 reparse 대상은 내려가지 않습니다.
/// </summary>
public sealed class ReadOnlyDirectorySizeService : IReadOnlyDirectorySizeService
{
    private const int CancellationCheckInterval = 64;

    public Task<long> CalculateSizeAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => CalculateSizeCore(path, cancellationToken), cancellationToken);

    private static long CalculateSizeCore(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        string expanded;
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        }
        catch
        {
            return 0;
        }

        string full;
        try
        {
            full = Path.GetFullPath(expanded);
        }
        catch
        {
            return 0;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(full))
        {
            return TryFileLength(full, cancellationToken);
        }

        if (!Directory.Exists(full))
        {
            return 0;
        }

        return SumDirectoryTree(full, cancellationToken);
    }

    private static long TryFileLength(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0)
            {
                return 0;
            }

            return ClampLength(new FileInfo(filePath).Length);
        }
        catch (PathTooLongException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (SecurityException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static long SumDirectoryTree(string rootDirectory, CancellationToken cancellationToken)
    {
        try
        {
            if ((File.GetAttributes(rootDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                return 0;
            }
        }
        catch
        {
            return 0;
        }

        var enumOptions = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        var stack = new Stack<string>();
        stack.Push(rootDirectory);
        long total = 0;
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

                        len = ClampLength(len);
                        try
                        {
                            total = checked(total + len);
                        }
                        catch (OverflowException)
                        {
                            return long.MaxValue;
                        }
                    }
                    catch (PathTooLongException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                    catch (SecurityException)
                    {
                    }
                    catch (IOException)
                    {
                        // deleted / locked between enumerate and read
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
                        // skip
                    }
                }
            }
            catch (PathTooLongException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (SecurityException)
            {
            }
            catch (IOException)
            {
            }
        }

        return total;
    }

    private static long ClampLength(long len) => len < 0 ? 0 : len;
}
