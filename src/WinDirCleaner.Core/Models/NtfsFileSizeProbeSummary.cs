namespace WinDirCleaner.Core.Models;

public sealed class NtfsFileSizeProbeSummary
{
    public NtfsFileSizeProbeSummary(
        int requestedSampleCount,
        int attemptedCount,
        int successCount,
        int accessDeniedCount,
        int notFoundCount,
        int failedCount,
        long totalSampledSizeBytes,
        TimeSpan elapsed,
        double filesPerSecond,
        double successRate,
        double accessDeniedRate,
        double failureRate)
    {
        ValidateNonNegative(nameof(requestedSampleCount), requestedSampleCount);
        ValidateNonNegative(nameof(attemptedCount), attemptedCount);
        ValidateNonNegative(nameof(successCount), successCount);
        ValidateNonNegative(nameof(accessDeniedCount), accessDeniedCount);
        ValidateNonNegative(nameof(notFoundCount), notFoundCount);
        ValidateNonNegative(nameof(failedCount), failedCount);
        if (totalSampledSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalSampledSizeBytes), totalSampledSizeBytes, "Cannot be negative.");
        }

        if (filesPerSecond < 0 || double.IsNaN(filesPerSecond) || double.IsInfinity(filesPerSecond))
        {
            throw new ArgumentOutOfRangeException(nameof(filesPerSecond), filesPerSecond, "Must be a non-negative finite value.");
        }

        ValidateRate(nameof(successRate), successRate);
        ValidateRate(nameof(accessDeniedRate), accessDeniedRate);
        ValidateRate(nameof(failureRate), failureRate);

        RequestedSampleCount = requestedSampleCount;
        AttemptedCount = attemptedCount;
        SuccessCount = successCount;
        AccessDeniedCount = accessDeniedCount;
        NotFoundCount = notFoundCount;
        FailedCount = failedCount;
        TotalSampledSizeBytes = totalSampledSizeBytes;
        Elapsed = elapsed;
        FilesPerSecond = filesPerSecond;
        SuccessRate = successRate;
        AccessDeniedRate = accessDeniedRate;
        FailureRate = failureRate;
    }

    public int RequestedSampleCount { get; }

    public int AttemptedCount { get; }

    public int SuccessCount { get; }

    public int AccessDeniedCount { get; }

    public int NotFoundCount { get; }

    public int FailedCount { get; }

    public long TotalSampledSizeBytes { get; }

    public TimeSpan Elapsed { get; }

    public double FilesPerSecond { get; }

    public double SuccessRate { get; }

    public double AccessDeniedRate { get; }

    public double FailureRate { get; }

    private static void ValidateNonNegative(string name, int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value cannot be negative.");
        }
    }

    private static void ValidateRate(string name, double value)
    {
        if (value < 0 || value > 1 || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "Rate must be between 0 and 1.");
        }
    }
}
