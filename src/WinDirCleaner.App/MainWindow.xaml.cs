using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Binding = System.Windows.Data.Binding;
using System.Windows.Threading;
using WinDirCleaner.App.Services;
using WinDirCleaner.App.ViewModels;
using WinDirCleaner.Core.Formatting;
using WinDirCleaner.Core.Models;
using WinDirCleaner.Core.Services;

namespace WinDirCleaner.App;

public partial class MainWindow : Window
{
    private readonly IDriveInfoService _driveInfoService = new DriveInfoService();
    private readonly IStorageAnalysisService _storageAnalysisService = new StorageAnalysisService();
    private readonly INtfsFastScanProbeService _ntfsFastScanProbeService = new NtfsFastScanProbeService();
    private readonly INtfsFastScanTreeProbeService _ntfsFastScanTreeProbeService = new NtfsFastScanTreeProbeService();
    private readonly INtfsFileSizeProbeService _ntfsFileSizeProbeService = new NtfsFileSizeProbeService();
    private readonly ICleanupCandidateService _cleanupCandidateService = new CleanupCandidatePreviewService();
    private readonly CleanupCandidateDetectionService _cleanupCandidateDetection = new();
    private readonly ICleanupPreviewService _cleanupPreviewService = new CleanupPreviewService();

    private bool _cleanupDetectionRunning;
    private bool _cleanupPreviewRunning;

    private readonly ObservableCollection<DriveSummaryViewModel> _drives = new();
    private readonly ObservableCollection<StorageAnalysisItemViewModel> _analysisResults = new();
    private readonly ObservableCollection<CleanupItemViewModel> _cleanupCandidates = new();

    private CancellationTokenSource? _analysisCts;
    private int _analysisOpId;
    private bool _analysisRunning;
    private bool _driveLoading;
    private int _driveLoadGeneration;
    private int _pendingDriveCapacityTasks;
    private bool _initialLoadCompleted;

    private bool _syncingDetailSelection;

    private long _lastUiFilesScanned;
    private long _lastUiDirectoriesScanned;
    private long _lastUiBytesScanned;
    private TimeSpan _lastUiElapsed;
    private long _lastScanningUiApplyTicks;
    private const double MinUiScanningMilliseconds = 400.0;

    private StorageAnalysisPerformanceSummary? _lastPerformanceSummary;

    private long? _lastNtfsTreeFileRecords;

    private bool _demoMode;

    private int _ntfsUiBusyCount;

    private bool _applyingDemoDriveUi;

    private static readonly string DemoNtfsResultLead =
        "〔DEMO〕 표시용 예시입니다. IOCTL·진단 서비스는 호출하지 않았습니다.\r\n\r\n";

    public MainWindow()
    {
        InitializeComponent();
        DriveList.ItemsSource = _drives;
        AnalysisResultsGrid.ItemsSource = _analysisResults;
        CleanupCandidatesGrid.ItemsSource = _cleanupCandidates;
        ConfigureAnalysisColumns();
        RefillCleanupCandidatesPreview(useDemo: false);
        ParallelDegreeComboBox.Items.Add(2);
        ParallelDegreeComboBox.Items.Add(3);
        ParallelDegreeComboBox.Items.Add(4);
        ParallelDegreeComboBox.SelectedIndex = 0;
        UpdateAnalysisButtonsIdle();
        UpdateNtfsFileSizeBenchmarkHintVisibility();
        UpdateDemoChromeVisuals();
        ResetCleanupPreviewUi();
        UpdateCleanupPreviewButtonState();
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        DriveLoadStatusText.Visibility = Visibility.Visible;
        DriveLoadProgress.Visibility = Visibility.Visible;
        DriveLoadStatusText.Text = "창을 표시했습니다. 드라이브 목록을 준비합니다…";
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await LoadDrivesAsync();
    }

    private void ConfigureAnalysisColumns()
    {
        AnalysisResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "이름",
            Binding = new Binding(nameof(StorageAnalysisItemViewModel.Name)),
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
        });
        AnalysisResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "유형",
            Binding = new Binding(nameof(StorageAnalysisItemViewModel.EntryTypeText)),
            Width = 80,
        });
        AnalysisResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "크기",
            Binding = new Binding(nameof(StorageAnalysisItemViewModel.SizeText)),
            Width = 110,
        });
        AnalysisResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "파일 수",
            Binding = new Binding(nameof(StorageAnalysisItemViewModel.FileCountText)),
            Width = 80,
        });
        AnalysisResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "폴더 수",
            Binding = new Binding(nameof(StorageAnalysisItemViewModel.DirectoryCountText)),
            Width = 80,
        });
        AnalysisResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "비고",
            Binding = new Binding(nameof(StorageAnalysisItemViewModel.NoteText)),
            Width = new DataGridLength(1.6, DataGridLengthUnitType.Star),
        });
    }

    private async void RefreshDrivesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_demoMode)
        {
            return;
        }

        await LoadDrivesAsync();
    }

    private async Task LoadDrivesAsync()
    {
        if (_demoMode)
        {
            ApplyDemoDataToUi();
            return;
        }

        InvalidateAnalysisSessionAndCancel();

        var generation = Interlocked.Increment(ref _driveLoadGeneration);

        _analysisResults.Clear();
        ClearBothGridSelections();
        UpdateRightPanelPrimary();

        ClearAnalysisPerformanceSection();

        LoadErrorText.Visibility = Visibility.Collapsed;
        LoadErrorText.Text = string.Empty;

        _driveLoading = true;
        DriveLoadStatusText.Text = "드라이브 목록을 불러오는 중입니다…";
        RefreshDriveLoadingBanner();
        ApplyInteractionChromeState();
        FooterAnalysisStateText.Text = "상태: 빠른 개요 — 드라이브 목록 준비";

        try
        {
            DriveList.SelectedItem = null;
            IReadOnlyList<DriveBasicInfo> basics;
            try
            {
                basics = await Task.Run(() => _driveInfoService.GetOrderedDriveBasicInfos()).ConfigureAwait(true);
            }
            catch (Exception)
            {
                _drives.Clear();
                DriveList.SelectedItem = null;
                LoadErrorText.Text = "드라이브 목록을 불러오지 못했습니다.";
                LoadErrorText.Visibility = Visibility.Visible;
                LastRefreshText.Text = "마지막 드라이브 새로고침: (오류)";
                return;
            }

            if (generation != Volatile.Read(ref _driveLoadGeneration))
            {
                return;
            }

            _drives.Clear();
            foreach (var b in basics)
            {
                _drives.Add(new DriveSummaryViewModel(b));
            }

            FooterPhaseText.Text = "읽기 전용 — 드라이브 요약";
            FooterDeleteNote.Text = "삭제·정리 실행 기능 없음";
            LastRefreshText.Text = "마지막 드라이브 새로고침: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

            foreach (var b in basics)
            {
                if (b.InitialCapacityStatus == DriveLoadStatus.Skipped)
                {
                    continue;
                }

                _ = RunDriveCapacityProbeAsync(b, generation);
            }
        }
        catch (Exception)
        {
            _drives.Clear();
            DriveList.SelectedItem = null;
            LoadErrorText.Text = "드라이브 정보를 불러오지 못했습니다.";
            LoadErrorText.Visibility = Visibility.Visible;
            LastRefreshText.Text = "마지막 드라이브 새로고침: (오류)";
        }
        finally
        {
            _driveLoading = false;
            _initialLoadCompleted = true;
            RefreshDriveLoadingBanner();
            ApplyInteractionChromeState();
            if (!_demoMode)
            {
                RefillCleanupCandidatesPreview(useDemo: false);
            }
        }

        UpdateDriveContextText();
        UpdateAnalysisIdleMessage();
        UpdateFooterAnalysisStateIdle();
        UpdateAnalysisButtonsIdle();
    }

    private async Task RunDriveCapacityProbeAsync(DriveBasicInfo info, int generation)
    {
        if (_demoMode)
        {
            return;
        }

        Interlocked.Increment(ref _pendingDriveCapacityTasks);
        RefreshDriveLoadingBanner();

        try
        {
            var result = await _driveInfoService
                .ProbeDriveCapacityAsync(info.Name, info.DriveType, TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);

            await Dispatcher.InvokeAsync(
                () =>
                {
                    if (generation != Volatile.Read(ref _driveLoadGeneration))
                    {
                        return;
                    }

                    DriveSummaryViewModel? vm = null;
                    foreach (var d in _drives)
                    {
                        if (string.Equals(d.Name, info.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            vm = d;
                            break;
                        }
                    }

                    vm?.ApplyProbeResult(result);
                },
                DispatcherPriority.Background);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingDriveCapacityTasks);
            await Dispatcher.InvokeAsync(
                () =>
                {
                    RefreshDriveLoadingBanner();
                    ApplyInteractionChromeState();
                });
        }
    }

    private void RefreshDriveLoadingBanner()
    {
        if (_demoMode)
        {
            DriveLoadStatusText.Visibility = Visibility.Collapsed;
            DriveLoadProgress.Visibility = Visibility.Collapsed;
            UpdateFooterAnalysisStateIdle();
            return;
        }

        if (_driveLoading)
        {
            DriveLoadStatusText.Visibility = Visibility.Visible;
            DriveLoadProgress.Visibility = Visibility.Visible;
            DriveLoadProgress.IsIndeterminate = true;
            UpdateFooterAnalysisStateIdle();
            return;
        }

        var pending = Volatile.Read(ref _pendingDriveCapacityTasks);
        if (pending > 0)
        {
            DriveLoadStatusText.Visibility = Visibility.Visible;
            DriveLoadProgress.Visibility = Visibility.Visible;
            DriveLoadProgress.IsIndeterminate = true;
            DriveLoadStatusText.Text = string.Create(
                CultureInfo.CurrentCulture,
                $"일부 드라이브 용량을 확인하는 중입니다… (남은 조회: {pending})");
            UpdateFooterAnalysisStateIdle();
            return;
        }

        DriveLoadStatusText.Visibility = Visibility.Collapsed;
        DriveLoadProgress.Visibility = Visibility.Collapsed;
        UpdateFooterAnalysisStateIdle();
    }

    private void ApplyInteractionChromeState()
    {
        var running = _analysisRunning;
        var loading = _driveLoading;
        var ntfsBusy = Volatile.Read(ref _ntfsUiBusyCount) > 0;
        var pendingCap = Volatile.Read(ref _pendingDriveCapacityTasks);
        var demoToggleOk = !loading && !running && !ntfsBusy && pendingCap == 0 && !_cleanupPreviewRunning;
        DemoModeCheckBox.IsEnabled = demoToggleOk;

        var demo = _demoMode;
        var previewBusy = _cleanupPreviewRunning;
        RefreshDrivesButton.IsEnabled = !demo && !loading && !running && !previewBusy;
        DriveList.IsEnabled = !loading && !running && !previewBusy;
        var sel = DriveList.SelectedItem as DriveSummaryViewModel;
        StartAnalysisButton.IsEnabled =
            !demo && !loading && !running && !previewBusy && sel is { IsDetailedAnalysisEnabled: true };
        ExperimentalFastDetailCheckBox.IsEnabled = !demo && !loading && !running && !previewBusy;
        ParallelDegreeComboBox.IsEnabled =
            !demo && !loading && !running && !previewBusy && ExperimentalFastDetailCheckBox.IsChecked == true;
        NtfsFastScanDiagButton.IsEnabled = !demo && !loading && !running && !previewBusy;
        NtfsFastScanTreeDiagButton.IsEnabled = !demo && !loading && !running && !previewBusy;
        NtfsFileSizeSampleCountComboBox.IsEnabled = !demo && !loading && !running && !previewBusy;
        NtfsFileSizeProbeButton.IsEnabled = !demo && !loading && !running && !previewBusy;
        CleanupRefreshButton.IsEnabled = !demo && !loading && !running && !_cleanupDetectionRunning && !previewBusy;
        UpdateCleanupPreviewButtonState();
    }

    private void ExperimentalFastDetailCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e) =>
        ApplyInteractionChromeState();

    private StorageAnalysisOptions BuildStorageAnalysisOptions()
    {
        if (ExperimentalFastDetailCheckBox.IsChecked != true)
        {
            return new StorageAnalysisOptions
            {
                Mode = StorageAnalysisMode.Sequential,
                MaxDegreeOfParallelism = 1,
            };
        }

        var dop = 2;
        if (ParallelDegreeComboBox.SelectedItem is int selectedDop)
        {
            dop = selectedDop;
        }
        else if (ParallelDegreeComboBox.SelectedIndex >= 0)
        {
            dop = ParallelDegreeComboBox.SelectedIndex + 2;
        }

        return new StorageAnalysisOptions
        {
            Mode = StorageAnalysisMode.LimitedParallel,
            MaxDegreeOfParallelism = dop,
        };
    }

    private static string FormatCompletedAnalysisModePhrase(StorageAnalysisPerformanceSummary? summary)
    {
        if (summary is null)
        {
            return "순차 모드";
        }

        return summary.AnalysisMode == StorageAnalysisMode.LimitedParallel
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"병렬 모드 · 동시 작업 {summary.MaxDegreeOfParallelism}개")
            : "순차 모드";
    }

    private void DriveList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingDemoDriveUi)
        {
            return;
        }

        InvalidateAnalysisSessionAndCancel();

        _analysisResults.Clear();
        ClearAnalysisGridSelectionOnly();
        UpdateRightPanelPrimary();

        ClearAnalysisPerformanceSection();

        if (_demoMode)
        {
            RefillDemoAnalysisResults();
            UpdateDriveContextText();
            UpdateAnalysisIdleMessage();
            UpdateFooterAnalysisStateIdle();
            UpdateAnalysisButtonsIdle();
            return;
        }

        UpdateDriveContextText();
        UpdateAnalysisIdleMessage();
        UpdateFooterAnalysisStateIdle();
        UpdateAnalysisButtonsIdle();
    }

    private async void StartAnalysisButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DriveList.SelectedItem is not DriveSummaryViewModel driveVm)
        {
            return;
        }

        if (!driveVm.IsDetailedAnalysisEnabled)
        {
            return;
        }

        await ExecuteDetailedAnalysisAsync(driveVm.Name);
    }

    private async Task ExecuteDetailedAnalysisAsync(string rootPathForAnalysis)
    {
        if (_demoMode)
        {
            return;
        }

        var opId = Interlocked.Increment(ref _analysisOpId);

        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();
        var token = _analysisCts.Token;

        _analysisResults.Clear();
        ClearBothGridSelections();
        UpdateRightPanelPrimary();

        ResetAnalysisUiStatsSnapshot();
        _lastPerformanceSummary = null;

        ClearAnalysisPerformanceSection();

        SetAnalysisRunningUi(true);
        ShowAnalysisProgressPanel();

        var displayPath = rootPathForAnalysis.Trim();
        var analysisOptions = BuildStorageAnalysisOptions();
        var modeLead = analysisOptions.Mode == StorageAnalysisMode.LimitedParallel
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"상세 분석 중: 병렬 모드, 동시 작업 {analysisOptions.MaxDegreeOfParallelism}개. ")
            : "상세 분석 중: 순차 모드. ";
        AnalysisStateText.Text = modeLead +
            $"선택한 드라이브: {displayPath}. 큰 드라이브는 몇 분 이상 걸릴 수 있습니다.";
        FooterAnalysisStateText.Text = "상태: 드라이브 상세 분석 중";

        var progress = CreateAnalysisProgressHandler(opId);

        try
        {
            var items = await _storageAnalysisService.AnalyzeTopLevelAsync(
                rootPathForAnalysis,
                analysisOptions,
                progress,
                token);
            if (opId != Volatile.Read(ref _analysisOpId))
            {
                return;
            }

            if (items.Count != _analysisResults.Count)
            {
                _analysisResults.Clear();
                foreach (var item in items)
                {
                    _analysisResults.Add(new StorageAnalysisItemViewModel(item));
                }
            }

            FinalSortAnalysisResultsOnce();

            var summaryBody = BuildThroughputSummaryBody(
                _lastUiElapsed,
                _lastUiFilesScanned,
                _lastUiDirectoriesScanned,
                _lastUiBytesScanned,
                _lastPerformanceSummary);
            var modePhrase = FormatCompletedAnalysisModePhrase(_lastPerformanceSummary);

            AnalysisStateText.Text =
                "드라이브 상세 분석이 완료되었습니다. " + FormatElapsedHuman(_lastUiElapsed) + " · " + modePhrase + " — " + summaryBody
                + " 표시된 크기는 집계값입니다.";
            FooterAnalysisStateText.Text = "상태: 드라이브 상세 분석 완료 — " + modePhrase + " — " + summaryBody;

            LastAnalysisTimeText.Text = "마지막 상세 분석 완료: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        }
        catch (OperationCanceledException)
        {
            if (opId != Volatile.Read(ref _analysisOpId))
            {
                return;
            }

            var summaryBody = BuildThroughputSummaryBody(
                _lastUiElapsed,
                _lastUiFilesScanned,
                _lastUiDirectoriesScanned,
                _lastUiBytesScanned,
                _lastPerformanceSummary);
            var modePhrase = FormatCompletedAnalysisModePhrase(_lastPerformanceSummary);

            AnalysisStateText.Text =
                "드라이브 상세 분석이 취소되었습니다. " + modePhrase + ". 취소 시점까지: " + summaryBody
                + "까지 확인했습니다. 현재까지 집계된 결과는 목록에 남아 있을 수 있습니다.";
            FooterAnalysisStateText.Text = "상태: 드라이브 상세 분석 취소됨 — " + modePhrase + " — " + summaryBody;
        }
        catch (Exception ex)
        {
            if (opId != Volatile.Read(ref _analysisOpId))
            {
                return;
            }

            _analysisResults.Clear();
            ClearAnalysisPerformanceSection();

            AnalysisStateText.Text =
                "드라이브 상세 분석 실패: " + FormatElapsedHuman(_lastUiElapsed) + " 후 오류가 발생했습니다. 일부 항목은 확인되지 않았을 수 있습니다.";
            FooterAnalysisStateText.Text = "상태: 드라이브 상세 분석 실패 — " + ex.Message;
        }
        finally
        {
            if (opId == Volatile.Read(ref _analysisOpId))
            {
                HideAnalysisProgressPanel();
                SetAnalysisRunningUi(false);
            }
        }
    }

    private IProgress<StorageAnalysisProgress> CreateAnalysisProgressHandler(int opId) =>
        new Progress<StorageAnalysisProgress>(p =>
        {
            if (opId != Volatile.Read(ref _analysisOpId))
            {
                return;
            }

            ApplyAnalysisProgress(p);
        });

    private static string ActiveAnalysisProgressTitleStem() => "드라이브 상세 분석";

    private void ApplyAnalysisProgress(StorageAnalysisProgress p)
    {
        CaptureScanStatsFromProgress(p);

        switch (p.Kind)
        {
            case StorageAnalysisProgressKind.Started:
                _lastScanningUiApplyTicks = 0;
                ShowAnalysisProgressPanel();
                AnalysisProgressBar.IsIndeterminate = false;
                AnalysisProgressBar.Maximum = 100;
                AnalysisProgressBar.Value = 0;
                AnalysisProgressTitleText.Text = ActiveAnalysisProgressTitleStem() + " 진행";
                AnalysisProgressCurrentText.Text = string.IsNullOrWhiteSpace(p.Message)
                    ? "준비 중…"
                    : p.Message;
                AnalysisProgressRatioText.Text = $"최상위 항목: 0 / {p.TotalTopLevelItems}";
                AnalysisProgressPercentText.Text = "진행률: 0.0%";
                ApplyScanningProgressVisuals(p);
                AnalysisProgressElapsedText.Text = "경과: " + FormatElapsed(p.Elapsed);
                break;

            case StorageAnalysisProgressKind.TopLevelItemStarted:
                AnalysisProgressCurrentText.Text = "현재: " + (p.CurrentTopLevelName ?? "—");
                AnalysisProgressRatioText.Text = $"최상위 항목: {p.CompletedTopLevelItems} / {p.TotalTopLevelItems}";
                AnalysisProgressBar.Value = p.TopLevelProgressPercent;
                AnalysisProgressPercentText.Text = $"진행률: {p.TopLevelProgressPercent:0.0}%";
                ApplyScanningProgressVisuals(p);
                AnalysisProgressElapsedText.Text = "경과: " + FormatElapsed(p.Elapsed);
                break;

            case StorageAnalysisProgressKind.Scanning:
                AnalysisProgressRatioText.Text = $"최상위 항목: {p.CompletedTopLevelItems} / {p.TotalTopLevelItems}";
                AnalysisProgressBar.Value = p.TopLevelProgressPercent;
                AnalysisProgressPercentText.Text = $"진행률: {p.TopLevelProgressPercent:0.0}%";
                ApplyScanningProgressVisuals(p);
                AnalysisProgressElapsedText.Text = "경과: " + FormatElapsed(p.Elapsed);
                if (ShouldApplyScanningPathTextNow())
                {
                    AnalysisProgressCurrentText.Text =
                        "현재: " + (p.CurrentTopLevelName ?? "—") + (string.IsNullOrEmpty(p.CurrentPath) ? string.Empty : $" · {p.CurrentPath}");
                }

                break;

            case StorageAnalysisProgressKind.TopLevelItemCompleted:
                if (p.CompletedItem is not null)
                {
                    InsertAnalysisResultSorted(new StorageAnalysisItemViewModel(p.CompletedItem));
                }

                AnalysisProgressCurrentText.Text = "완료: " + (p.CurrentTopLevelName ?? "—");
                AnalysisProgressRatioText.Text = $"최상위 항목: {p.CompletedTopLevelItems} / {p.TotalTopLevelItems}";
                AnalysisProgressBar.Value = p.TopLevelProgressPercent;
                AnalysisProgressPercentText.Text = $"진행률: {p.TopLevelProgressPercent:0.0}%";
                ApplyScanningProgressVisuals(p);
                AnalysisProgressElapsedText.Text = "경과: " + FormatElapsed(p.Elapsed);
                break;

            case StorageAnalysisProgressKind.Completed:
                _lastPerformanceSummary = p.PerformanceSummary;
                AnalysisProgressBar.Value = p.TotalTopLevelItems <= 0 ? 0 : 100;
                AnalysisProgressPercentText.Text =
                    p.TotalTopLevelItems <= 0 ? "진행률: —" : "진행률: 100.0%";
                ApplyScanningProgressVisuals(p);
                AnalysisProgressElapsedText.Text = "경과: " + FormatElapsed(p.Elapsed);
                AnalysisProgressTitleText.Text = ActiveAnalysisProgressTitleStem() + " 완료";
                AnalysisProgressCurrentText.Text =
                    ActiveAnalysisProgressTitleStem() + " 완료: " + BuildThroughputSummaryBody(p.Elapsed, p.FilesScanned, p.DirectoriesScanned, p.BytesScanned, p.PerformanceSummary);
                if (p.PerformanceSummary is not null)
                {
                    PopulatePerformanceSection(p.PerformanceSummary);
                }

                break;

            case StorageAnalysisProgressKind.Cancelled:
                _lastPerformanceSummary = p.PerformanceSummary;
                FooterAnalysisStateText.Text =
                    "상태: 드라이브 상세 분석 취소됨 — "
                    + BuildThroughputSummaryBody(p.Elapsed, p.FilesScanned, p.DirectoriesScanned, p.BytesScanned, p.PerformanceSummary);
                AnalysisProgressTitleText.Text = ActiveAnalysisProgressTitleStem() + " 취소";
                ApplyScanningProgressVisuals(p);
                AnalysisProgressElapsedText.Text = "경과: " + FormatElapsed(p.Elapsed);
                AnalysisProgressCurrentText.Text =
                    "취소 시점: " + BuildThroughputSummaryBody(p.Elapsed, p.FilesScanned, p.DirectoriesScanned, p.BytesScanned, p.PerformanceSummary);
                if (p.PerformanceSummary is not null)
                {
                    PopulatePerformanceSection(p.PerformanceSummary);
                }

                break;
        }
    }

    private void ResetAnalysisUiStatsSnapshot()
    {
        _lastUiFilesScanned = 0;
        _lastUiDirectoriesScanned = 0;
        _lastUiBytesScanned = 0;
        _lastUiElapsed = TimeSpan.Zero;
        _lastScanningUiApplyTicks = 0;
    }

    private void CaptureScanStatsFromProgress(StorageAnalysisProgress p)
    {
        _lastUiFilesScanned = p.FilesScanned;
        _lastUiDirectoriesScanned = p.DirectoriesScanned;
        _lastUiBytesScanned = p.BytesScanned;
        _lastUiElapsed = p.Elapsed;
    }

    private bool ShouldApplyScanningPathTextNow()
    {
        var now = Stopwatch.GetTimestamp();
        var minTicks = (long)(MinUiScanningMilliseconds * Stopwatch.Frequency / 1000.0);
        if (_lastScanningUiApplyTicks != 0 && now - _lastScanningUiApplyTicks < minTicks)
        {
            return false;
        }

        _lastScanningUiApplyTicks = now;
        return true;
    }

    private void ApplyScanningProgressVisuals(StorageAnalysisProgress p)
    {
        AnalysisProgressScanStatsText.Text =
            $"스캔한 파일: {p.FilesScanned:N0}개 · 스캔한 폴더: {p.DirectoriesScanned:N0}개 · 누적 확인 용량: {ByteSizeFormatter.Format(p.BytesScanned)}";
    }

    private static string BuildThroughputSummaryBody(
        TimeSpan elapsed,
        long filesScanned,
        long directoriesScanned,
        long bytesScanned,
        StorageAnalysisPerformanceSummary? summary = null)
    {
        var core = string.Create(
            CultureInfo.CurrentCulture,
            $"{FormatElapsedHuman(elapsed)} · 파일 {filesScanned:N0}개 · 폴더 {directoriesScanned:N0}개 · 확인 용량 {ByteSizeFormatter.Format(bytesScanned)}");

        if (summary is null)
        {
            return core;
        }

        return core + " · 평균 " + FormatAverageScanRates(summary);
    }

    private static string FormatAverageScanRates(StorageAnalysisPerformanceSummary summary)
    {
        var bytesRounded = (long)Math.Round(summary.BytesPerSecond);
        return string.Create(
            CultureInfo.CurrentCulture,
            $"{summary.FilesPerSecond:F0} files/s · {summary.DirectoriesPerSecond:F0} dirs/s · {ByteSizeFormatter.Format(bytesRounded)}/s");
    }

    private void ClearAnalysisPerformanceSection()
    {
        _lastPerformanceSummary = null;
        AnalysisPerformancePanel.Visibility = Visibility.Collapsed;
        AnalysisPerformanceSummaryBodyText.Text = string.Empty;
        AnalysisSlowTopItemsText.Text = string.Empty;
        AnalysisPerformanceNoteText.Text = string.Empty;
    }

    private void PopulatePerformanceSection(StorageAnalysisPerformanceSummary summary)
    {
        var modeLine = summary.AnalysisMode == StorageAnalysisMode.LimitedParallel
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"분석 방식: 병렬 · 동시 작업 {summary.MaxDegreeOfParallelism}개")
            : "분석 방식: 순차 · 동시 작업 1개(병렬 미사용)";

        AnalysisPerformanceSummaryBodyText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"전체 소요 {FormatElapsedHuman(summary.TotalElapsed)} · 최상위 완료 {summary.CompletedTopLevelItemCount}/{summary.PlannedTopLevelItemCount}개 · 접근 불가·건너뜀 표시 {summary.InaccessibleTopLevelCount:N0}개 · 파일 {summary.TotalFilesScanned:N0}개 · 폴더 {summary.TotalDirectoriesScanned:N0}개 · 확인 용량 {ByteSizeFormatter.Format(summary.TotalBytesScanned)} · 평균 {FormatAverageScanRates(summary)}{Environment.NewLine}{modeLine}");

        var top = _analysisResults
            .OrderByDescending(x => x.TopLevelAnalysisDuration)
            .Take(5)
            .ToList();

        AnalysisSlowTopItemsText.Text = top.Count == 0
            ? "(표시할 최상위 항목이 없습니다.)"
            : string.Join(Environment.NewLine, top.Select((vm, i) => FormatSlowTopLine(vm, i + 1)));

        AnalysisPerformanceNoteText.Text =
            "일반 파일 열거 방식이라 범위가 크면 시간이 걸릴 수 있습니다.";

        AnalysisPerformancePanel.Visibility = Visibility.Visible;
    }

    private static string FormatSlowTopLine(StorageAnalysisItemViewModel vm, int rank)
    {
        var dur = FormatElapsedHuman(vm.TopLevelAnalysisDuration);
        return string.Create(
            CultureInfo.CurrentCulture,
            $"{rank}. {vm.Name} — {dur} — {vm.SizeText} — 파일 {vm.FileCount:N0}개 · 폴더 {vm.DirectoryCount:N0}개");
    }

    private static string FormatElapsedHuman(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1.0)
        {
            var totalHours = (int)elapsed.TotalHours;
            return string.Create(CultureInfo.InvariantCulture, $"{totalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}");
        }

        if (elapsed.TotalSeconds < 60.0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{elapsed.TotalSeconds:F1}초");
        }

        return elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void InsertAnalysisResultSorted(StorageAnalysisItemViewModel item)
    {
        for (var i = 0; i < _analysisResults.Count; i++)
        {
            var cur = _analysisResults[i];
            var cmp = item.SizeBytes.CompareTo(cur.SizeBytes);
            if (cmp > 0)
            {
                _analysisResults.Insert(i, item);
                return;
            }

            if (cmp == 0 && string.Compare(item.Name, cur.Name, StringComparison.OrdinalIgnoreCase) < 0)
            {
                _analysisResults.Insert(i, item);
                return;
            }
        }

        _analysisResults.Add(item);
    }

    private void FinalSortAnalysisResultsOnce()
    {
        var sorted = _analysisResults
            .OrderByDescending(x => x.SizeBytes)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _analysisResults.Clear();
        foreach (var vm in sorted)
        {
            _analysisResults.Add(vm);
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        var totalHours = (int)elapsed.TotalHours;
        return totalHours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{totalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}")
            : elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void ShowAnalysisProgressPanel() => AnalysisProgressPanel.Visibility = Visibility.Visible;

    private void HideAnalysisProgressPanel() => AnalysisProgressPanel.Visibility = Visibility.Collapsed;

    private void CancelAnalysisButton_OnClick(object sender, RoutedEventArgs e)
    {
        AnalysisStateText.Text = "드라이브 상세 분석 취소를 요청했습니다. 잠시만 기다려 주세요.";
        FooterAnalysisStateText.Text = "상태: 드라이브 상세 분석 취소 요청 중…";
        _analysisCts?.Cancel();
    }

    private void AnalysisResultsGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingDetailSelection)
        {
            return;
        }

        _syncingDetailSelection = true;
        try
        {
            CleanupCandidatesGrid.SelectedItem = null;
            UpdateRightPanelPrimary();
        }
        finally
        {
            _syncingDetailSelection = false;
        }
    }

    private void CleanupCandidatesGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingDetailSelection)
        {
            return;
        }

        _syncingDetailSelection = true;
        try
        {
            AnalysisResultsGrid.SelectedItem = null;
            UpdateRightPanelPrimary();
        }
        finally
        {
            _syncingDetailSelection = false;
        }
    }

    private string GetNtfsFastScanDiagnosticRootPath()
    {
        if (DriveList.SelectedItem is DriveSummaryViewModel vm && vm.IsDetailedAnalysisEnabled)
        {
            var n = vm.Name.Trim();
            return n.EndsWith('\\') ? n : n + "\\";
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var root = Path.GetPathRoot(windows);
        return string.IsNullOrEmpty(root) ? "C:\\" : root;
    }

    private int GetNtfsFileSizeSampleCount()
    {
        if (NtfsFileSizeSampleCountComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return Math.Clamp(n, 1, NtfsFileSizeProbeService.MaxSampleCount);
        }

        return 500;
    }

    private void NtfsFileSizeSampleCountComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateNtfsFileSizeBenchmarkHintVisibility();

    private void UpdateNtfsFileSizeBenchmarkHintVisibility()
    {
        if (NtfsFileSizeBenchmarkHintText is null || NtfsFileSizeSampleCountComboBox is null)
        {
            return;
        }

        var n = GetNtfsFileSizeSampleCount();
        NtfsFileSizeBenchmarkHintText.Visibility = n >= 5000 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetNtfsDiagnosticSummary(string text)
    {
        if (NtfsDiagnosticSummaryText is not null)
        {
            NtfsDiagnosticSummaryText.Text = text;
        }
    }

    private static string FormatNtfsStatusLabel(NtfsFastScanStatus status) =>
        status switch
        {
            NtfsFastScanStatus.Completed => "진단 완료",
            NtfsFastScanStatus.AccessDenied => "권한 문제로 진단을 완료하지 못했습니다.",
            NtfsFastScanStatus.NotNtfs => "NTFS 아님",
            NtfsFastScanStatus.Failed => "진단 실패",
            NtfsFastScanStatus.ApiUnavailable => "API 사용 불가",
            NtfsFastScanStatus.Supported => "지원됨",
            NtfsFastScanStatus.NotStarted => "시작 전",
            _ => status.ToString(),
        };

    private static string FormatElapsedHumanNtfs(TimeSpan t)
    {
        if (t.TotalSeconds < 10)
        {
            return t.TotalSeconds.ToString("F2", CultureInfo.CurrentCulture) + "초";
        }

        if (t.TotalMinutes < 1)
        {
            return t.TotalSeconds.ToString("F1", CultureInfo.CurrentCulture) + "초";
        }

        return string.Create(CultureInfo.CurrentCulture, $"{(int)t.TotalMinutes}분 {t.Seconds}초");
    }

    private static string InterpretNtfsFastScanProbe(NtfsFastScanProbeResult r) =>
        r.Status switch
        {
            NtfsFastScanStatus.Completed when r.RecordsRead > 0 =>
                "해석: 레코드 열거가 성공한 것으로 보입니다. 다음 진단에서 트리 골격을 확인할 수 있습니다.",
            NtfsFastScanStatus.Completed =>
                "해석: 읽은 레코드 수가 0입니다. 볼륨 상태를 한 번 더 확인해 보세요.",
            NtfsFastScanStatus.AccessDenied =>
                "해석: 권한 문제로 볼륨 진단을 열 수 없습니다.",
            NtfsFastScanStatus.NotNtfs =>
                "해석: NTFS 볼륨이 아니어서 이 진단을 사용할 수 없습니다.",
            NtfsFastScanStatus.Failed =>
                "해석: 진단을 완료하지 못했습니다. 일반 상세 분석은 계속 사용할 수 있습니다.",
            _ =>
                "해석: 상태를 확인한 뒤 필요하면 다시 실행해 보세요.",
        };

    private static string BuildRecordsReadSummary(NtfsFastScanProbeResult r)
    {
        var line1 = string.Create(
            CultureInfo.CurrentCulture,
            $"레코드 열거 진단 · {FormatNtfsStatusLabel(r.Status)} · {FormatElapsedHumanNtfs(r.Elapsed)}");
        var line2 = InterpretNtfsFastScanProbe(r).Replace("해석: ", string.Empty, StringComparison.Ordinal);
        return line1 + Environment.NewLine + line2;
    }

    private static string InterpretNtfsTreeNonCompleted(NtfsFastScanStatus status) =>
        status switch
        {
            NtfsFastScanStatus.AccessDenied =>
                "해석: 권한 문제로 트리 진단을 마치지 못했을 수 있습니다. 일반 상세 분석은 그대로 쓸 수 있습니다.",
            NtfsFastScanStatus.NotNtfs =>
                "해석: NTFS가 아니어서 트리 진단을 쓸 수 없습니다.",
            NtfsFastScanStatus.Failed =>
                "해석: 트리 진단을 끝까지 수행하지 못했습니다.",
            _ =>
                "해석: 결과 상태를 확인한 뒤 필요하면 다시 실행해 보세요.",
        };

    private static string InterpretNtfsTree(NtfsFastScanTreeProbeResult r)
    {
        if (r.Status != NtfsFastScanStatus.Completed)
        {
            return InterpretNtfsTreeNonCompleted(r.Status);
        }

        var s = r.Summary;
        var parts = new List<string>();
        if (s.UnsupportedVersionRecords == 0 && s.InvalidRecords == 0)
        {
            parts.Add("USN_RECORD 파싱 상태는 양호해 보입니다.");
        }

        if (s.UnsupportedVersionRecords > 0)
        {
            parts.Add("지원하지 않는 USN_RECORD 버전이 있습니다.");
        }

        if (s.InvalidRecords > 0)
        {
            parts.Add("일부 레코드 파싱에 실패했습니다.");
        }

        var orphanRatio = s.ParsedRecords > 0 ? (double)s.OrphanRecords / s.ParsedRecords : 0.0;
        if (orphanRatio > 0.05)
        {
            parts.Add("부모를 찾지 못한 레코드 비중이 큽니다. 트리 재구성은 추가 검토가 필요할 수 있습니다.");
        }
        else if (s.ParsedRecords > 0 && s.OrphanRecords <= Math.Max(50L, (long)(s.ParsedRecords * 0.001)))
        {
            parts.Add("대부분 레코드가 부모와 연결된 것으로 보입니다.");
        }

        if (parts.Count == 0)
        {
            parts.Add("요약을 만들기에 정보가 부족합니다.");
        }

        return "해석: " + string.Join(" ", parts);
    }

    private static string BuildTreeSummary(NtfsFastScanTreeProbeResult r)
    {
        var line1 = string.Create(
            CultureInfo.CurrentCulture,
            $"트리 골격 진단 · {FormatNtfsStatusLabel(r.Status)} · {FormatElapsedHumanNtfs(r.Summary.Elapsed)}");
        var line2 = InterpretNtfsTree(r).Replace("해석: ", string.Empty, StringComparison.Ordinal);
        return line1 + Environment.NewLine + line2;
    }

    private static string InterpretNtfsFileSize(NtfsFileSizeProbeResult r)
    {
        if (r.Status != NtfsFastScanStatus.Completed)
        {
            return r.Status switch
            {
                NtfsFastScanStatus.AccessDenied =>
                    "해석: 권한 문제로 샘플 진단을 마치지 못했을 수 있습니다. 일반 상세 분석은 그대로 쓸 수 있습니다.",
                NtfsFastScanStatus.NotNtfs =>
                    "해석: NTFS가 아니어서 샘플 진단을 쓸 수 없습니다.",
                NtfsFastScanStatus.Failed =>
                    "해석: 샘플 진단을 끝까지 수행하지 못했습니다.",
                _ =>
                    "해석: 결과 상태를 확인한 뒤 필요하면 다시 실행해 보세요.",
            };
        }

        var s = r.Summary;
        var parts = new List<string>();
        if (s.SuccessRate >= 0.98)
        {
            parts.Add("샘플 기준으로 크기 조회 성공률이 높은 편입니다.");
        }

        if (s.AccessDeniedRate > 0.05)
        {
            parts.Add("권한 때문에 크기를 읽지 못한 파일이 있습니다.");
        }

        if (s.FailureRate > 0.05)
        {
            parts.Add("일부 파일 크기 조회가 실패했습니다.");
        }

        parts.Add("아래 전체 시간은 샘플 속도로 곱해 본 추정치이며, 실제 전체 조회는 아직 실행하지 않았습니다.");
        return "해석: " + string.Join(" ", parts);
    }

    private static string BuildFileSizeSummary(NtfsFileSizeProbeResult r)
    {
        var s = r.Summary;
        var line1 = string.Create(
            CultureInfo.CurrentCulture,
            $"파일 크기 샘플 진단 · {FormatNtfsStatusLabel(r.Status)} · {FormatElapsedHumanNtfs(s.Elapsed)} · {s.SuccessCount:N0}/{s.AttemptedCount:N0} 성공");
        var line2 = InterpretNtfsFileSize(r).Replace("해석: ", string.Empty, StringComparison.Ordinal);
        return line1 + Environment.NewLine + line2;
    }

    private static string FormatNtfsFastScanDiagnosticResult(NtfsFastScanProbeResult r)
    {
        var lines = new List<string>
        {
            "상태: " + FormatNtfsStatusLabel(r.Status),
            "루트: " + r.RootPath,
            "볼륨 장치: " + r.VolumePath,
            "NTFS로 식별: " + (r.IsNtfs ? "예" : "아니오"),
            "읽은 레코드 수: " + r.RecordsRead.ToString("N0", CultureInfo.CurrentCulture),
            "소요: " + r.Elapsed.TotalSeconds.ToString("F3", CultureInfo.CurrentCulture) + "초",
        };

        if (!string.IsNullOrEmpty(r.ErrorMessage))
        {
            lines.Add("오류: " + r.ErrorMessage);
        }

        if (!string.IsNullOrEmpty(r.DetailMessage))
        {
            lines.Add("상세: " + r.DetailMessage);
        }

        lines.Add(InterpretNtfsFastScanProbe(r));
        return string.Join(Environment.NewLine, lines);
    }

    private string FormatNtfsFileSizeProbeResult(NtfsFileSizeProbeResult r)
    {
        var s = r.Summary;
        var lines = new List<string>
        {
            "상태: " + FormatNtfsStatusLabel(r.Status),
            "루트: " + r.RootPath,
            "볼륨 장치: " + r.VolumePath,
            "NTFS로 식별: " + (r.IsNtfs ? "예" : "아니오"),
            "소요: " + s.Elapsed.TotalSeconds.ToString("F3", CultureInfo.CurrentCulture) + "초",
            "요청 샘플 수: " + s.RequestedSampleCount.ToString("N0", CultureInfo.CurrentCulture),
            "시도(열기) 수: " + s.AttemptedCount.ToString("N0", CultureInfo.CurrentCulture),
            "성공: " + s.SuccessCount.ToString("N0", CultureInfo.CurrentCulture),
            "AccessDenied: " + s.AccessDeniedCount.ToString("N0", CultureInfo.CurrentCulture),
            "NotFound: " + s.NotFoundCount.ToString("N0", CultureInfo.CurrentCulture),
            "기타 실패: " + s.FailedCount.ToString("N0", CultureInfo.CurrentCulture),
            "성공률: " + FormatPercentOneLine(s.SuccessRate),
            "권한 거부: " + FormatPercentOneLine(s.AccessDeniedRate),
            "실패률: " + FormatPercentOneLine(s.FailureRate),
            "샘플 합계 크기: " + ByteSizeFormatter.Format(s.TotalSampledSizeBytes),
            "샘플 처리 속도: " + s.FilesPerSecond.ToString("F0", CultureInfo.CurrentCulture) + " files/s",
        };

        if (_lastNtfsTreeFileRecords is { } treeFileCount &&
            treeFileCount > 0 &&
            s.FilesPerSecond > 0)
        {
            var estSeconds = treeFileCount / s.FilesPerSecond;
            var est = TimeSpan.FromSeconds(estSeconds);
            lines.Add(string.Empty);
            lines.Add("[전체 조회 추정 · 샘플 기반]");
            lines.Add(
                "기준 파일 수: " +
                treeFileCount.ToString("N0", CultureInfo.CurrentCulture) +
                " (직전 트리 골격 진단의 FileRecords) · 샘플 처리 속도: " +
                s.FilesPerSecond.ToString("F0", CultureInfo.CurrentCulture) +
                " files/s · 전체 조회 추정(선형): 약 " +
                FormatApproxDuration(est));
            lines.Add("이 값은 샘플 기반 추정이며, 실제 전체 파일 크기 조회는 아직 실행하지 않았습니다.");
        }
        else
        {
            lines.Add(string.Empty);
            lines.Add("[전체 조회 추정]");
            lines.Add("「트리 골격 진단」을 완료하면 FileRecords 수와 샘플 속도로 추정 시간을 적습니다.");
        }

        lines.Add(string.Empty);
        lines.Add("USN 순서 편향이 남을 수 있고, 5,000건 이상은 stride로 구간을 넓힙니다.");

        if (!string.IsNullOrEmpty(r.ErrorMessage))
        {
            lines.Add(string.Empty);
            lines.Add("오류: " + r.ErrorMessage);
        }

        if (!string.IsNullOrEmpty(r.DetailMessage))
        {
            lines.Add("상세: " + r.DetailMessage);
        }

        if (r.Samples.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("샘플(일부):");
            foreach (var x in r.Samples.Take(15))
            {
                var sizeLine = x.Success && x.SizeBytes.HasValue
                    ? ByteSizeFormatter.Format(x.SizeBytes.Value)
                    : (x.ErrorMessage ?? "실패");
                lines.Add($"  • {x.Name} | {sizeLine}");
            }
        }

        lines.Add(string.Empty);
        lines.Add(InterpretNtfsFileSize(r));
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatPercentOneLine(double rate) =>
        (rate * 100).ToString("F1", CultureInfo.CurrentCulture) + "%";

    private static string FormatApproxDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{(int)t.TotalHours}시간 {t.Minutes}분");
        }

        if (t.TotalMinutes >= 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{(int)t.TotalMinutes}분 {t.Seconds}초");
        }

        return t.TotalSeconds.ToString("F1", CultureInfo.CurrentCulture) + "초";
    }

    private static string FormatNtfsFastScanTreeDiagnosticResult(NtfsFastScanTreeProbeResult r)
    {
        var s = r.Summary;
        var lines = new List<string>
        {
            "상태: " + FormatNtfsStatusLabel(r.Status),
            "루트: " + r.RootPath,
            "볼륨 장치: " + r.VolumePath,
            "NTFS로 식별: " + (r.IsNtfs ? "예" : "아니오"),
            "소요: " + s.Elapsed.TotalSeconds.ToString("F3", CultureInfo.CurrentCulture) + "초",
            "TotalRecords(원시 슬롯): " + s.TotalRecords.ToString("N0", CultureInfo.CurrentCulture),
            "ParsedRecords(고유 FRN): " + s.ParsedRecords.ToString("N0", CultureInfo.CurrentCulture),
            "FileRecords: " + s.FileRecords.ToString("N0", CultureInfo.CurrentCulture),
            "DirectoryRecords: " + s.DirectoryRecords.ToString("N0", CultureInfo.CurrentCulture),
            "ReparsePointRecords: " + s.ReparsePointRecords.ToString("N0", CultureInfo.CurrentCulture),
            "UnsupportedVersionRecords: " + s.UnsupportedVersionRecords.ToString("N0", CultureInfo.CurrentCulture),
            "InvalidRecords: " + s.InvalidRecords.ToString("N0", CultureInfo.CurrentCulture),
            "LinkedRecords: " + s.LinkedRecords.ToString("N0", CultureInfo.CurrentCulture),
            "OrphanRecords: " + s.OrphanRecords.ToString("N0", CultureInfo.CurrentCulture),
            "RootCandidateRecords: " + s.RootCandidateRecords.ToString("N0", CultureInfo.CurrentCulture),
            string.Empty,
            "파일 크기/폴더 용량은 아직 계산하지 않습니다.",
        };

        if (!string.IsNullOrEmpty(r.ErrorMessage))
        {
            lines.Add("오류: " + r.ErrorMessage);
        }

        if (!string.IsNullOrEmpty(r.DetailMessage))
        {
            lines.Add("상세: " + r.DetailMessage);
        }

        if (r.SampleRootNames.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("SampleRootNames(일부): " + string.Join(", ", r.SampleRootNames));
        }

        if (r.SampleRecords.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("SampleRecords(일부):");
            foreach (var rec in r.SampleRecords.Take(12))
            {
                lines.Add(
                    $"  • {rec.Name} | FRN={rec.FileReferenceNumber} parent={rec.ParentFileReferenceNumber} kind={rec.Kind}");
            }
        }

        lines.Add(string.Empty);
        lines.Add(InterpretNtfsTree(r));
        return string.Join(Environment.NewLine, lines);
    }

    private async void NtfsFastScanTreeDiagButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_demoMode)
        {
            return;
        }

        BeginNtfsUiOperation();
        var root = GetNtfsFastScanDiagnosticRootPath();
        NtfsFastScanTreeDiagResultText.Text = "NTFS 트리 골격 진단 중…";
        NtfsFastScanTreeDiagButton.IsEnabled = false;
        try
        {
            var result = await _ntfsFastScanTreeProbeService.ProbeTreeAsync(root).ConfigureAwait(true);
            if (result.Status == NtfsFastScanStatus.Completed)
            {
                _lastNtfsTreeFileRecords = result.Summary.FileRecords;
            }

            NtfsFastScanTreeDiagResultText.Text = FormatNtfsFastScanTreeDiagnosticResult(result);
            SetNtfsDiagnosticSummary(BuildTreeSummary(result));
        }
        catch (Exception ex)
        {
            NtfsFastScanTreeDiagResultText.Text = "진단 실패: " + ex.Message;
            SetNtfsDiagnosticSummary("트리 골격 진단 · 진단 실패" + Environment.NewLine + ex.Message);
        }
        finally
        {
            EndNtfsUiOperation();
        }
    }

    private async void NtfsFastScanDiagButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_demoMode)
        {
            return;
        }

        BeginNtfsUiOperation();
        var root = GetNtfsFastScanDiagnosticRootPath();
        NtfsFastScanDiagResultText.Text = "NTFS 레코드 열거 중…";
        NtfsFastScanDiagButton.IsEnabled = false;
        try
        {
            var result = await _ntfsFastScanProbeService.ProbeAsync(root).ConfigureAwait(true);
            NtfsFastScanDiagResultText.Text = FormatNtfsFastScanDiagnosticResult(result);
            SetNtfsDiagnosticSummary(BuildRecordsReadSummary(result));
        }
        catch (Exception ex)
        {
            NtfsFastScanDiagResultText.Text = "진단 실패: " + ex.Message;
            SetNtfsDiagnosticSummary("레코드 열거 진단 · 진단 실패" + Environment.NewLine + ex.Message);
        }
        finally
        {
            EndNtfsUiOperation();
        }
    }

    private async void NtfsFileSizeProbeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_demoMode)
        {
            return;
        }

        BeginNtfsUiOperation();
        var root = GetNtfsFastScanDiagnosticRootPath();
        var n = GetNtfsFileSizeSampleCount();
        NtfsFileSizeProbeResultText.Text = "파일 크기 샘플 진단 중…";
        NtfsFileSizeProbeButton.IsEnabled = false;
        NtfsFileSizeSampleCountComboBox.IsEnabled = false;
        try
        {
            var result = await _ntfsFileSizeProbeService.ProbeFileSizesAsync(root, n).ConfigureAwait(true);
            NtfsFileSizeProbeResultText.Text = FormatNtfsFileSizeProbeResult(result);
            SetNtfsDiagnosticSummary(BuildFileSizeSummary(result));
        }
        catch (Exception ex)
        {
            NtfsFileSizeProbeResultText.Text = "진단 실패: " + ex.Message;
            SetNtfsDiagnosticSummary("파일 크기 샘플 진단 · 진단 실패" + Environment.NewLine + ex.Message);
        }
        finally
        {
            EndNtfsUiOperation();
        }
    }

    private void ResetNtfsDiagnosticPlaceholderTexts()
    {
        const string idle = "아직 실행하지 않았습니다.";
        NtfsFastScanDiagResultText.Text = idle;
        NtfsFastScanTreeDiagResultText.Text = idle;
        NtfsFileSizeProbeResultText.Text = idle;
        SetNtfsDiagnosticSummary("아직 실험 진단을 실행하지 않았습니다.");
    }

    private void BeginNtfsUiOperation()
    {
        Interlocked.Increment(ref _ntfsUiBusyCount);
        ApplyInteractionChromeState();
    }

    private void EndNtfsUiOperation()
    {
        Interlocked.Decrement(ref _ntfsUiBusyCount);
        ApplyInteractionChromeState();
    }

    private void UpdateDemoChromeVisuals()
    {
        if (DemoModeBadgeBorder is null || DemoModeHintText is null)
        {
            return;
        }

        var vis = _demoMode ? Visibility.Visible : Visibility.Collapsed;
        DemoModeBadgeBorder.Visibility = vis;
        DemoModeHintText.Visibility = vis;
        if (DemoModeAnalysisNoticeText is not null)
        {
            DemoModeAnalysisNoticeText.Visibility = vis;
        }
    }

    private void RefillDemoAnalysisResults()
    {
        var driveName = DriveList.SelectedItem is DriveSummaryViewModel d ? d.Name : null;
        foreach (var item in DemoDataService.GetDemoTopLevelItemsForDrive(driveName))
        {
            _analysisResults.Add(new StorageAnalysisItemViewModel(item));
        }
    }

    private void ApplyDemoDataToUi()
    {
        _applyingDemoDriveUi = true;
        try
        {
            _drives.Clear();
            foreach (var s in DemoDataService.GetDemoDriveSummaries())
            {
                _drives.Add(new DriveSummaryViewModel(s));
            }

            _analysisResults.Clear();
            RefillDemoAnalysisResults();
            AnalysisResultsGrid.SelectedItem = null;
            RefillCleanupCandidatesPreview(useDemo: true);
            CleanupCandidatesGrid.SelectedItem = null;
            UpdateRightPanelPrimary();

            ClearAnalysisPerformanceSection();

            NtfsFastScanDiagResultText.Text = DemoNtfsResultLead + DemoDataService.GetDemoNtfsEnumUsnText();
            NtfsFastScanTreeDiagResultText.Text = DemoNtfsResultLead + DemoDataService.GetDemoNtfsTreeText();
            NtfsFileSizeProbeResultText.Text = DemoNtfsResultLead + DemoDataService.GetDemoNtfsFileSizeText();
            _lastNtfsTreeFileRecords = DemoDataService.GetDemoTreeFileRecordsForEstimate();
            SetNtfsDiagnosticSummary("데모 · 샘플만 표시 · 실제 진단 미실행");

            LoadErrorText.Visibility = Visibility.Collapsed;
            LastRefreshText.Text = "데모 — 실제 드라이브 목록 없음(샘플만)";
            HideAnalysisProgressPanel();

            DriveList.SelectedItem = _drives.Count > 0 ? _drives[0] : null;
        }
        finally
        {
            _applyingDemoDriveUi = false;
        }

        RefreshDriveLoadingBanner();
        UpdateDriveContextText();
        UpdateAnalysisIdleMessage();
        UpdateFooterAnalysisStateIdle();
        UpdateDemoChromeVisuals();
        ApplyInteractionChromeState();
    }

    private async void DemoModeCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var wantDemo = DemoModeCheckBox.IsChecked == true;
            if (wantDemo == _demoMode)
            {
                return;
            }

            if (wantDemo)
            {
                _demoMode = true;
                Interlocked.Increment(ref _driveLoadGeneration);
                InvalidateAnalysisSessionAndCancel();
                ApplyDemoDataToUi();
            }
            else
            {
                _demoMode = false;
                Interlocked.Increment(ref _driveLoadGeneration);
                InvalidateAnalysisSessionAndCancel();
                _lastNtfsTreeFileRecords = null;
                ResetNtfsDiagnosticPlaceholderTexts();
                UpdateDemoChromeVisuals();
                await LoadDrivesAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            AnalysisStateText.Text = "데모 모드 전환 중 오류: " + ex.Message;
        }
    }

    private void InvalidateAnalysisSessionAndCancel()
    {
        Interlocked.Increment(ref _analysisOpId);
        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = null;
    }

    private void SetAnalysisRunningUi(bool running)
    {
        _analysisRunning = running;
        CancelAnalysisButton.IsEnabled = running;
        ApplyInteractionChromeState();
    }

    private void UpdateAnalysisButtonsIdle()
    {
        _analysisRunning = false;
        CancelAnalysisButton.IsEnabled = false;
        ApplyInteractionChromeState();
    }

    private void UpdateDriveContextText()
    {
        if (_demoMode)
        {
            if (DriveList.SelectedItem is DriveSummaryViewModel demoVm)
            {
                RightDriveContextText.Text =
                    $"데모: 선택된 드라이브 {demoVm.Name} ({demoVm.Label}). 실제 경로가 아닙니다.";
            }
            else
            {
                RightDriveContextText.Text =
                    "데모 모드입니다. 왼쪽 목록에서 샘플 드라이브를 선택해 보세요.";
            }

            return;
        }

        if (DriveList.SelectedItem is DriveSummaryViewModel vm)
        {
            if (!vm.IsDetailedAnalysisEnabled)
            {
                RightDriveContextText.Text =
                    $"선택된 위치: {vm.Name} ({vm.DriveTypeDisplay}). 이 유형은 상세 분석(최상위 폴더 열거) 대상이 아닙니다. 고정 또는 이동식 드라이브를 선택하세요.";
            }
            else
            {
                RightDriveContextText.Text =
                    $"선택된 드라이브: {vm.Name} ({vm.Label}).";
            }
        }
        else
        {
            RightDriveContextText.Text =
                "선택된 드라이브가 없습니다. 고정/이동식 드라이브를 선택한 뒤 「드라이브 상세 분석」으로 최상위 항목을 집계할 수 있습니다.";
        }
    }

    private void UpdateAnalysisIdleMessage()
    {
        if (!_initialLoadCompleted)
        {
            AnalysisStateText.Text = "빠른 개요를 준비하는 중입니다…";
            return;
        }

        if (_demoMode)
        {
            AnalysisStateText.Text = "데모: 샘플만 표시 중 — 디스크 미접근.";
            return;
        }

        if (DriveList.SelectedItem is DriveSummaryViewModel vm)
        {
            if (!vm.IsDetailedAnalysisEnabled)
            {
                AnalysisStateText.Text =
                    $"선택한 {vm.Name}은(는) 상세 분석 대상이 아닙니다. 고정 또는 이동식 드라이브를 선택하세요.";
            }
            else
            {
                AnalysisStateText.Text =
                    $"드라이브 {vm.Name}이(가) 선택되었습니다. 「드라이브 상세 분석」으로 최상위 항목을 집계할 수 있습니다.";
            }
        }
        else
        {
            AnalysisStateText.Text = "고정·이동식 드라이브를 고른 뒤 「드라이브 상세 분석」을 시작하세요.";
        }
    }

    private void UpdateFooterAnalysisStateIdle()
    {
        if (_demoMode)
        {
            FooterAnalysisStateText.Text = "상태: 데모 — 샘플만 표시";
            return;
        }

        if (_driveLoading)
        {
            FooterAnalysisStateText.Text = "상태: 빠른 개요 — 드라이브 목록 준비";
            return;
        }

        var pending = Volatile.Read(ref _pendingDriveCapacityTasks);
        if (pending > 0)
        {
            FooterAnalysisStateText.Text =
                string.Create(CultureInfo.CurrentCulture, $"상태: 빠른 개요 — 용량 조회 진행 중(남은 조회 {pending})");
            return;
        }

        if (DriveList.SelectedItem is DriveSummaryViewModel vm)
        {
            FooterAnalysisStateText.Text = vm.IsDetailedAnalysisEnabled
                ? "상태: 드라이브 선택됨 — 상세 분석 대기"
                : "상태: 드라이브 선택됨 — 상세 분석 비대상 유형";
        }
        else
        {
            FooterAnalysisStateText.Text = "상태: 드라이브를 선택한 뒤 상세 분석을 시작하세요.";
        }
    }

    private void ClearBothGridSelections()
    {
        if (_syncingDetailSelection)
        {
            return;
        }

        _syncingDetailSelection = true;
        try
        {
            AnalysisResultsGrid.SelectedItem = null;
            CleanupCandidatesGrid.SelectedItem = null;
        }
        finally
        {
            _syncingDetailSelection = false;
        }
    }

    private void ClearAnalysisGridSelectionOnly()
    {
        if (_syncingDetailSelection)
        {
            return;
        }

        _syncingDetailSelection = true;
        try
        {
            AnalysisResultsGrid.SelectedItem = null;
        }
        finally
        {
            _syncingDetailSelection = false;
        }
    }

    private void ReplaceCleanupVms(IReadOnlyList<CleanupItem> items, bool isStaticPreview)
    {
        foreach (var vm in _cleanupCandidates)
        {
            vm.SelectionChanged -= CleanupItemViewModel_OnSelectionChanged;
        }

        _cleanupCandidates.Clear();

        foreach (var item in items)
        {
            var vm = new CleanupItemViewModel(item, isStaticPreview);
            vm.SelectionChanged += CleanupItemViewModel_OnSelectionChanged;
            _cleanupCandidates.Add(vm);
        }

        CleanupCandidatesGrid.SelectedItem = null;
        RefreshCleanupSelectionSummary();
        ResetCleanupPreviewUi();
        UpdateCleanupPreviewButtonState();
        UpdateRightPanelPrimary();
    }

    private void RefillCleanupCandidatesPreview(bool useDemo)
    {
        var items = useDemo ? DemoDataService.GetDemoCleanupPreviewItems() : _cleanupCandidateService.GetPreviewCandidates();
        ReplaceCleanupVms(items, isStaticPreview: true);
        CleanupDetectionStatusText.Text = useDemo
            ? "데모 모드에서는 샘플 후보만 표시합니다."
            : "정적 미리보기를 표시 중입니다. 「정리 후보 새로고침」으로 일부 경로 크기를 읽기 전용으로 확인할 수 있습니다.";
    }

    private async void CleanupRefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_demoMode)
        {
            CleanupDetectionStatusText.Text = "데모 모드에서는 샘플 후보만 표시합니다.";
            return;
        }

        _cleanupDetectionRunning = true;
        CleanupRefreshButton.IsEnabled = false;
        CleanupDetectionStatusText.Text = "정리 후보 확인 중…";
        ApplyInteractionChromeState();
        try
        {
            using var cts = new CancellationTokenSource();
            var items = await _cleanupCandidateDetection.DetectCandidatesAsync(cts.Token).ConfigureAwait(true);
            ReplaceCleanupVms(items, isStaticPreview: false);
            CleanupDetectionStatusText.Text = "정리 후보 확인 완료";
        }
        catch (OperationCanceledException)
        {
            CleanupDetectionStatusText.Text = "정리 후보 확인이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            CleanupDetectionStatusText.Text = "정리 후보 확인 중 오류: " + ex.Message;
        }
        finally
        {
            _cleanupDetectionRunning = false;
            ApplyInteractionChromeState();
        }
    }

    private void CleanupItemViewModel_OnSelectionChanged(object? sender, EventArgs e) => RefreshCleanupSelectionSummary();

    private void RefreshCleanupSelectionSummary()
    {
        var selected = _cleanupCandidates.Where(x => x.IsSelected).ToList();
        var count = selected.Count;
        long sum = 0;
        var counted = 0;
        foreach (var x in selected)
        {
            if (x.Item.SizeBytes > 0)
            {
                sum += x.Item.SizeBytes;
                counted++;
            }
        }

        var scopeTag = _cleanupCandidates.Any(x => !x.IsStaticPreview)
            ? "(읽기 전용 확인)"
            : "(미리보기)";

        if (count == 0)
        {
            CleanupSelectionSummaryText.Text = "선택 0개 · 합계 크기: —";
            UpdateCleanupPreviewButtonState();
            return;
        }

        if (counted == 0)
        {
            CleanupSelectionSummaryText.Text =
                string.Create(CultureInfo.CurrentCulture, $"선택 {count:N0}개 · 합계 크기: 미계산 항목만 포함 {scopeTag}");
            UpdateCleanupPreviewButtonState();
            return;
        }

        if (counted < count)
        {
            CleanupSelectionSummaryText.Text =
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"선택 {count:N0}개 · 합계(크기가 있는 항목만): {ByteSizeFormatter.Format(sum)} · 나머지는 미계산 {scopeTag}");
            UpdateCleanupPreviewButtonState();
            return;
        }

        CleanupSelectionSummaryText.Text =
            string.Create(CultureInfo.CurrentCulture, $"선택 {count:N0}개 · 합계 크기: {ByteSizeFormatter.Format(sum)} {scopeTag}");
        UpdateCleanupPreviewButtonState();
    }

    private void UpdateCleanupPreviewButtonState()
    {
        if (CleanupPreviewButton is null)
        {
            return;
        }

        var loading = _driveLoading;
        var running = _analysisRunning;
        var ntfsBusy = Volatile.Read(ref _ntfsUiBusyCount) > 0;
        if (_cleanupPreviewRunning)
        {
            CleanupPreviewButton.IsEnabled = false;
            return;
        }

        if (loading || running || ntfsBusy || _cleanupDetectionRunning)
        {
            CleanupPreviewButton.IsEnabled = false;
            return;
        }

        var eligible = _cleanupCandidates.Any(x =>
            x.IsSelected
            && x.Risk != CleanupRisk.Dangerous
            && x.IsSelectable
            && x.CanDelete);

        CleanupPreviewButton.IsEnabled = eligible;
    }

    private void UpdateRightPanelPrimary()
    {
        if (CleanupCandidatesGrid.SelectedItem is CleanupItemViewModel c)
        {
            ShowCleanupDetail(c);
            return;
        }

        if (AnalysisResultsGrid.SelectedItem is StorageAnalysisItemViewModel a)
        {
            ShowAnalysisDetail(a);
            return;
        }

        ShowPolicyOnly();
    }

    private void ShowPolicyOnly()
    {
        CleanupCandidateDetailPanel.Visibility = Visibility.Collapsed;
        AnalysisEntryDetailPanel.Visibility = Visibility.Collapsed;
        PolicyHeadingText.Visibility = Visibility.Visible;
        PolicyScrollViewer.Visibility = Visibility.Visible;
    }

    private void ShowCleanupDetail(CleanupItemViewModel c)
    {
        CleanupCandidateDetailPanel.Visibility = Visibility.Visible;
        AnalysisEntryDetailPanel.Visibility = Visibility.Collapsed;
        PolicyHeadingText.Visibility = Visibility.Collapsed;
        PolicyScrollViewer.Visibility = Visibility.Collapsed;

        CleanupDetailNameText.Text = c.Name;
        CleanupDetailRiskText.Text = "분류: " + c.RiskBadgeText + " · " + c.ProtectionLabel;
        CleanupDetailPathText.Text = "경로: " + c.Path;
        CleanupDetailDescriptionText.Text = "설명: " + c.Description;
        CleanupDetailReasonText.Text = "분류 이유: " + c.Reason;
        CleanupDetailImpactText.Text = "삭제 시 영향(참고): " + c.Impact;
        CleanupDetailStatusText.Text = "현재 상태: 미리보기 / 삭제 기능 없음";
        CleanupDetailDangerNoteText.Visibility = c.IsDangerous ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowAnalysisDetail(StorageAnalysisItemViewModel vm)
    {
        CleanupCandidateDetailPanel.Visibility = Visibility.Collapsed;
        AnalysisEntryDetailPanel.Visibility = Visibility.Visible;
        PolicyHeadingText.Visibility = Visibility.Collapsed;
        PolicyScrollViewer.Visibility = Visibility.Collapsed;

        DetailNameText.Text = vm.Name;
        DetailPathText.Text = "경로: " + vm.Path;
        DetailSizeText.Text = "크기: " + vm.SizeText;
        DetailTypeText.Text = "유형: " + vm.EntryTypeText;
        DetailCountsText.Text = $"파일 수: {vm.FileCountText}, 폴더 수: {vm.DirectoryCountText}, 접근 가능: {(vm.IsAccessible ? "예" : "아니오")}";
        DetailAnalysisPerfText.Text = vm.ItemAnalysisTimingText;
        DetailNoteText.Text = "비고: " + vm.NoteText;
    }

    private void ResetCleanupPreviewUi()
    {
        CleanupPreviewStatusText.Text = "—";
        CleanupPreviewSummaryText.Text = "아직 정리 미리보기를 실행하지 않았습니다.";
        CleanupPreviewMessagesText.Text = string.Empty;
        CleanupPreviewSampleText.Text = string.Empty;
    }

    private void ApplyCleanupPreviewResultToUi(CleanupPreviewResult result)
    {
        var s = result.Summary;
        var elapsedMs = (long)Math.Round(s.Elapsed.TotalMilliseconds);
        var fps = s.FilesPerSecond;

        CleanupPreviewSummaryText.Text = string.Join(
            Environment.NewLine,
            $"선택 후보 수: {s.SelectedCandidateCount:N0}",
            $"스캔한 후보 루트: {s.ScannedCandidateCount:N0}",
            $"제외·건너뜀(선택 기준): {s.SkippedCandidateCount:N0}",
            $"대상 파일 수: {s.TargetFileCount:N0}",
            $"대상 폴더 수(탐색한 디렉터리): {s.TargetDirectoryCount:N0}",
            $"접근 제한으로 건너뜀: {s.InaccessibleCount:N0}",
            $"기타 실패·건너뜀: {s.FailedCount:N0}",
            $"예상 크기 합계: {ByteSizeFormatter.Format(s.EstimatedBytes)}",
            $"소요 시간: {elapsedMs:N0} ms",
            $"대상 파일 처리 속도: {fps:F1} files/s");

        CleanupPreviewMessagesText.Text =
            result.Messages.Count > 0 ? string.Join(Environment.NewLine, result.Messages) : string.Empty;

        var lines = new List<string>();
        foreach (var t in result.SampleTargets.Take(20))
        {
            lines.Add($"{t.SourceCandidateName} · {t.Name} ({ByteSizeFormatter.Format(t.SizeBytes)})");
            lines.Add("  " + t.Path);
        }

        CleanupPreviewSampleText.Text = string.Join(Environment.NewLine, lines);
    }

    private List<CleanupItem> SnapshotCleanupItemsForPreview() =>
        _cleanupCandidates
            .Select(
                vm => new CleanupItem(
                    vm.Item.Id,
                    vm.Item.Name,
                    vm.Item.Path,
                    vm.Item.SizeBytes,
                    vm.Item.Risk,
                    vm.IsSelected,
                    vm.Item.Description,
                    vm.Item.Reason,
                    vm.Item.Impact))
            .ToList();

    private async void CleanupPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_cleanupPreviewRunning)
        {
            return;
        }

        _cleanupPreviewRunning = true;
        ApplyInteractionChromeState();
        UpdateCleanupPreviewButtonState();

        try
        {
            if (_demoMode)
            {
                CleanupPreviewStatusText.Text = "데모 모드: 샘플 미리보기 결과를 표시합니다.";
                ApplyCleanupPreviewResultToUi(DemoDataService.GetDemoCleanupPreviewResult());
                return;
            }

            CleanupPreviewStatusText.Text = "정리 미리보기 계산 중…";
            var snapshot = SnapshotCleanupItemsForPreview();
            var result = await _cleanupPreviewService.PreviewAsync(snapshot, CancellationToken.None).ConfigureAwait(true);
            ApplyCleanupPreviewResultToUi(result);
            CleanupPreviewStatusText.Text = "정리 미리보기 완료";
        }
        catch (OperationCanceledException)
        {
            CleanupPreviewStatusText.Text = "정리 미리보기가 취소되었습니다.";
        }
        catch (Exception ex)
        {
            CleanupPreviewStatusText.Text = "정리 미리보기 실패: " + ex.Message;
            CleanupPreviewSummaryText.Text = "미리보기를 완료하지 못했습니다.";
            CleanupPreviewMessagesText.Text = string.Empty;
            CleanupPreviewSampleText.Text = string.Empty;
        }
        finally
        {
            _cleanupPreviewRunning = false;
            ApplyInteractionChromeState();
            UpdateCleanupPreviewButtonState();
        }
    }
}
