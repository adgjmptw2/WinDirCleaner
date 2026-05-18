namespace WinDirCleaner.Core.Services;

/// <summary>
/// 디렉터리(또는 단일 파일) 용량을 읽기 전용으로 합산합니다. 파일 시스템을 변경하지 않습니다.
/// </summary>
public interface IReadOnlyDirectorySizeService
{
    Task<long> CalculateSizeAsync(string path, CancellationToken cancellationToken = default);
}
