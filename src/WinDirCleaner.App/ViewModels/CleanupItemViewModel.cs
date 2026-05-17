using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using WinDirCleaner.Core.Formatting;
using WinDirCleaner.Core.Models;

namespace WinDirCleaner.App.ViewModels;

public sealed class CleanupItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public CleanupItemViewModel(CleanupItem item)
    {
        Item = item;
        _isSelected = item.Selectable && item.Selected;
        if (!item.Selectable)
        {
            _isSelected = false;
        }
    }

    public CleanupItem Item { get; }

    public string Id => Item.Id;

    public string Name => Item.Name;

    public string Path => Item.Path;

    public CleanupRisk Risk => Item.Risk;

    public bool IsSelectable => Item.Selectable;

    public bool CanDelete => Item.CanDelete;

    public bool IsDangerous => Item.Risk == CleanupRisk.Dangerous;

    public string Description => Item.Description;

    public string Reason => Item.Reason;

    public string Impact => Item.Impact;

    public string SizeText =>
        Item.SizeBytes > 0
            ? ByteSizeFormatter.Format(Item.SizeBytes)
            : "미리보기(미계산)";

    public string RiskText => Item.Risk switch
    {
        CleanupRisk.Recommended => "권장",
        CleanupRisk.Optional => "선택",
        CleanupRisk.Dangerous => "위험",
        _ => Item.Risk.ToString(),
    };

    public string RiskBadgeText => IsDangerous ? RiskText + " · 보호됨" : RiskText;

    public string ProtectionLabel => IsDangerous ? "삭제 불가(보호됨)" : "선택 가능";

    public string SummaryForGrid
    {
        get
        {
            var d = Description;
            return d.Length <= 90 ? d : string.Concat(d.AsSpan(0, 87), "…");
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!IsSelectable)
            {
                return;
            }

            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
