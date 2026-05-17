using System.Globalization;
using WinDirCleaner.Core.Formatting;
using WinDirCleaner.Core.Models;

namespace WinDirCleaner.App.ViewModels;

public sealed class StorageAnalysisItemViewModel
{
    public StorageAnalysisItemViewModel(StorageAnalysisItem item)
    {
        Name = item.Name;
        Path = item.Path;
        SizeBytes = item.SizeBytes;
        FileCount = item.FileCount;
        DirectoryCount = item.DirectoryCount;
        TopLevelAnalysisDuration = item.TopLevelAnalysisDuration;
        SizeText = ByteSizeFormatter.Format(item.SizeBytes);
        EntryTypeText = item.EntryType switch
        {
            StorageEntryType.Directory => "폴더",
            StorageEntryType.File => "파일",
            _ => "기타",
        };
        FileCountText = item.FileCount.ToString(CultureInfo.InvariantCulture);
        DirectoryCountText = item.DirectoryCount.ToString(CultureInfo.InvariantCulture);
        var note = item.Note ?? string.Empty;
        Note = note;
        NoteText = string.IsNullOrWhiteSpace(note) ? "—" : note;
        IsAccessible = item.IsAccessible;
        ItemAnalysisTimingText = BuildItemAnalysisTimingText(item);
    }

    public int FileCount { get; }

    public int DirectoryCount { get; }

    public TimeSpan TopLevelAnalysisDuration { get; }
    public string ItemAnalysisTimingText { get; }

    public string Name { get; }

    public string Path { get; }

    public string SizeText { get; }

    public long SizeBytes { get; }

    public string EntryTypeText { get; }

    public string FileCountText { get; }

    public string DirectoryCountText { get; }

    public string Note { get; }
    public string NoteText { get; }

    public bool IsAccessible { get; }

    private static string BuildItemAnalysisTimingText(StorageAnalysisItem item)
    {
        var d = item.TopLevelAnalysisDuration;
        if (d <= TimeSpan.Zero)
        {
            return "이 항목 분석 소요: 측정값 없음(즉시 완료에 가깝습니다)";
        }

        var sec = d.TotalSeconds;
        var filesPerSec = sec > 0 ? item.FileCount / sec : 0d;
        var dirsPerSec = sec > 0 ? item.DirectoryCount / sec : 0d;
        var bytesPerSec = sec > 0 ? item.SizeBytes / sec : 0d;
        var bps = (long)Math.Round(bytesPerSec);
        return string.Create(
            CultureInfo.CurrentCulture,
            $"이 항목 분석 소요: {FormatItemDuration(d)} · 약 {filesPerSec:F0} files/s · 약 {dirsPerSec:F0} dirs/s · 약 {ByteSizeFormatter.Format(bps)}/s");
    }

    private static string FormatItemDuration(TimeSpan d)
    {
        if (d.TotalHours >= 1.0)
        {
            var h = (int)d.TotalHours;
            return string.Create(CultureInfo.InvariantCulture, $"{h}:{d.Minutes:D2}:{d.Seconds:D2}");
        }

        if (d.TotalSeconds < 60.0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{d.TotalSeconds:F2}초");
        }

        return d.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
}
