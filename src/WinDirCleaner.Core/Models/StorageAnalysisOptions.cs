namespace WinDirCleaner.Core.Models;

public sealed class StorageAnalysisOptions
{
    public const int MinParallelism = 1;

    public const int MaxParallelism = 4;

    public const int DefaultMaxDegreeOfParallelism = 2;
    public StorageAnalysisMode Mode { get; init; } = StorageAnalysisMode.Sequential;
    public int MaxDegreeOfParallelism { get; init; } = DefaultMaxDegreeOfParallelism;
    public static StorageAnalysisOptions Default { get; } = new();
    public static StorageAnalysisOptions Normalize(StorageAnalysisOptions? options)
    {
        if (options is null)
        {
            return Default;
        }

        var dop = Math.Clamp(options.MaxDegreeOfParallelism, MinParallelism, MaxParallelism);
        return new StorageAnalysisOptions
        {
            Mode = options.Mode,
            MaxDegreeOfParallelism = dop,
        };
    }
}
