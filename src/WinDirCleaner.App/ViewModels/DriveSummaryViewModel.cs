using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using WinDirCleaner.Core.Formatting;
using WinDirCleaner.Core.Models;

namespace WinDirCleaner.App.ViewModels;

public sealed class DriveSummaryViewModel : INotifyPropertyChanged
{
    private string _label;
    private string _totalText;
    private string _usedText;
    private string _freeText;
    private double _usedPercent;
    private string _usedPercentText;
    private DriveLoadStatus _capacityLoadStatus;
    private string _capacityStatusMessage;
    private bool _showCapacityProgress;

    public DriveSummaryViewModel(DriveBasicInfo basic)
    {
        Name = basic.Name;
        DriveType = basic.DriveType;
        DriveTypeDisplay = FormatDriveType(DriveType);
        _capacityLoadStatus = basic.InitialCapacityStatus;
        _capacityStatusMessage = basic.InitialStatusMessage;
        _label = DriveType is DriveType.Fixed or DriveType.Removable
            ? "볼륨 라벨: 확인 중…"
            : "—";

        if (basic.InitialCapacityStatus == DriveLoadStatus.Ready)
        {
            throw new ArgumentException("Ready 상태는 기본 목록에서 사용하지 않습니다.", nameof(basic));
        }

        if (basic.InitialCapacityStatus == DriveLoadStatus.Skipped)
        {
            _label = "—";
        }

        _totalText = "—";
        _usedText = "—";
        _freeText = "—";
        _usedPercent = 0;
        _usedPercentText = "—";
        _showCapacityProgress = false;
    }

    public DriveSummaryViewModel(DriveSummary summary)
    {
        Name = summary.Name;
        DriveType = DriveType.Fixed;
        DriveTypeDisplay = FormatDriveType(DriveType);
        _label = summary.Label;
        _totalText = ByteSizeFormatter.Format(summary.TotalBytes);
        _usedText = ByteSizeFormatter.Format(summary.UsedBytes);
        _freeText = ByteSizeFormatter.Format(summary.FreeBytes);

        var pct = summary.UsedPercent;
        if (!double.IsFinite(pct))
        {
            pct = 0;
        }

        _usedPercent = Math.Clamp(pct, 0, 100);
        _usedPercentText = _usedPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        _capacityLoadStatus = DriveLoadStatus.Ready;
        _capacityStatusMessage = string.Empty;
        _showCapacityProgress = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public DriveType DriveType { get; }

    public string DriveTypeDisplay { get; }
    public bool IsDetailedAnalysisEnabled =>
        DriveType is DriveType.Fixed or DriveType.Removable;

    public string Label
    {
        get => _label;
        private set => SetField(ref _label, value);
    }

    public string TotalText
    {
        get => _totalText;
        private set => SetField(ref _totalText, value);
    }

    public string UsedText
    {
        get => _usedText;
        private set => SetField(ref _usedText, value);
    }

    public string FreeText
    {
        get => _freeText;
        private set => SetField(ref _freeText, value);
    }

    public double UsedPercent
    {
        get => _usedPercent;
        private set => SetField(ref _usedPercent, value);
    }

    public string UsedPercentText
    {
        get => _usedPercentText;
        private set => SetField(ref _usedPercentText, value);
    }

    public DriveLoadStatus CapacityLoadStatus
    {
        get => _capacityLoadStatus;
        private set => SetField(ref _capacityLoadStatus, value);
    }

    public string CapacityStatusMessage
    {
        get => _capacityStatusMessage;
        private set => SetField(ref _capacityStatusMessage, value);
    }

    public bool ShowCapacityProgress
    {
        get => _showCapacityProgress;
        private set => SetField(ref _showCapacityProgress, value);
    }

    public void ApplyProbeResult(DriveCapacityProbeResult result)
    {
        CapacityLoadStatus = result.Status;
        CapacityStatusMessage = result.Message;

        switch (result.Status)
        {
            case DriveLoadStatus.Ready when result.Summary is not null:
                ApplySummary(result.Summary);
                break;

            case DriveLoadStatus.Timeout:
            case DriveLoadStatus.Failed:
            case DriveLoadStatus.NotReady:
                Label = "—";
                TotalText = UsedText = FreeText = "—";
                UsedPercent = 0;
                UsedPercentText = "—";
                ShowCapacityProgress = false;
                break;

            case DriveLoadStatus.Skipped:
                Label = "—";
                TotalText = UsedText = FreeText = "—";
                UsedPercent = 0;
                UsedPercentText = "—";
                ShowCapacityProgress = false;
                break;

            case DriveLoadStatus.Loading:
                break;

            default:
                if (result.Summary is null)
                {
                    Label = "—";
                    TotalText = UsedText = FreeText = "—";
                    UsedPercent = 0;
                    UsedPercentText = "—";
                    ShowCapacityProgress = false;
                }

                break;
        }
    }

    private void ApplySummary(DriveSummary summary)
    {
        Label = summary.Label;
        TotalText = ByteSizeFormatter.Format(summary.TotalBytes);
        UsedText = ByteSizeFormatter.Format(summary.UsedBytes);
        FreeText = ByteSizeFormatter.Format(summary.FreeBytes);

        var pct = summary.UsedPercent;
        if (!double.IsFinite(pct))
        {
            pct = 0;
        }

        UsedPercent = Math.Clamp(pct, 0, 100);
        UsedPercentText = UsedPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        ShowCapacityProgress = true;
    }

    private static string FormatDriveType(DriveType driveType) =>
        driveType switch
        {
            DriveType.Fixed => "고정 디스크",
            DriveType.Removable => "이동식",
            DriveType.Network => "네트워크",
            DriveType.CDRom => "CD/DVD",
            DriveType.Ram => "RAM 디스크",
            DriveType.NoRootDirectory => "루트 없음",
            _ => "기타",
        };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetField(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (field.Equals(value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void SetField(ref DriveLoadStatus field, DriveLoadStatus value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }
}
