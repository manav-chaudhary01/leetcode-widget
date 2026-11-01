using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using LeetCodeDesktopWidget.Helpers;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;

namespace LeetCodeDesktopWidget.ViewModels;

/// <summary>
/// Presentation logic and state manager connecting real LeetCode data, persistent caching,
/// configuration, and automatic background refresh to the desktop widget UI.
/// </summary>
public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILeetCodeService _leetCodeService;
    private readonly IConfigurationService _configService;
    private readonly ICacheService _cacheService;
    private readonly IRefreshCoordinator _refreshCoordinator;

    private string _username = "";
    private bool _isEditingUsername;
    private int _selectedYear;
    private YearlyActivity _currentActivity;
    private DailySubmission? _hoveredDay;
    private string _hoverInfoText = "Hover over a square to view submissions";
    private bool _isSyncing;
    private string _syncStatusText = "Not synced";
    private LeetCodeUserStats? _lastSuccessfulStats;
    private StreakInfo _streaks = StreakInfo.Empty;
    private YearlyStatistics _currentYearStats;
    private bool _isDisposed;
    private DateOnly? _customReferenceDate;

    public DateTime? ReferenceDateTime { get; set; }

    public DateOnly? ReferenceDate
    {
        get => ReferenceDateTime.HasValue ? DateOnly.FromDateTime(ReferenceDateTime.Value) : _customReferenceDate;
        set => _customReferenceDate = value;
    }

    public ObservableCollection<int> AvailableYears { get; } = new();

    public string Username
    {
        get => _username;
        set
        {
            if (_username != value)
            {
                _username = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsEditingUsername
    {
        get => _isEditingUsername;
        set
        {
            if (_isEditingUsername != value)
            {
                _isEditingUsername = value;
                OnPropertyChanged();
            }
        }
    }

    public int SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (_selectedYear != value)
            {
                _selectedYear = value;
                OnPropertyChanged();
                UpdateActivityForSelectedYear();
                _ = SaveConfigAsync();
            }
        }
    }

    public YearlyActivity CurrentActivity
    {
        get => _currentActivity;
        private set
        {
            _currentActivity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalSubmissions));
            OnPropertyChanged(nameof(ActiveDays));
            OnPropertyChanged(nameof(SummaryStatsText));
        }
    }

    public DailySubmission? HoveredDay
    {
        get => _hoveredDay;
        set
        {
            _hoveredDay = value;
            OnPropertyChanged();
            HoverInfoText = _hoveredDay != null
                ? _hoveredDay.GetDescription()
                : "Hover over a square to view submissions";
        }
    }

    public string HoverInfoText
    {
        get => _hoverInfoText;
        private set
        {
            _hoverInfoText = value;
            OnPropertyChanged();
        }
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        private set
        {
            _isSyncing = value;
            OnPropertyChanged();
        }
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        private set
        {
            _syncStatusText = value;
            OnPropertyChanged();
        }
    }

    // All-time Solved Counts
    public int TotalSolved => _lastSuccessfulStats?.TotalSolved ?? 0;
    public int EasySolved => _lastSuccessfulStats?.EasySolved ?? 0;
    public int MediumSolved => _lastSuccessfulStats?.MediumSolved ?? 0;
    public int HardSolved => _lastSuccessfulStats?.HardSolved ?? 0;
    public bool HasSolvedStats => _lastSuccessfulStats != null;

    // All-time Calculated Streaks (computed purely from daily submissions)
    public int CurrentStreak => _streaks.CurrentStreak;
    public int LongestStreak => _streaks.LongestStreak;

    // Selected-Year Statistics
    public YearlyStatistics CurrentYearStats => _currentYearStats;
    public int YearSubmissions => _currentYearStats.TotalSubmissions;
    public int YearActiveDays => _currentYearStats.ActiveDays;
    public string YearAverageText => _currentYearStats.ActiveDays > 0 ? $"{_currentYearStats.AveragePerActiveDay:F1}" : "0";
    public string MostActiveDayText => _currentYearStats.MostActiveDay?.ToString() ?? "None";
    public int YearLongestStreak => _currentYearStats.LongestStreak;

    public int TotalSubmissions => _currentActivity?.TotalSubmissions ?? 0;
    public int ActiveDays => _currentActivity?.ActiveDays ?? 0;

    public string SummaryStatsText => _currentActivity != null
        ? $"{_currentActivity.TotalSubmissions} submissions in {_currentActivity.Year}"
        : "";

    public ICommand RefreshCommand { get; }
    public ICommand ToggleEditUsernameCommand { get; }
    public ICommand SaveUsernameCommand { get; }

    public MainViewModel(
        ILeetCodeService? leetCodeService = null,
        IConfigurationService? configService = null,
        ICacheService? cacheService = null,
        IRefreshCoordinator? refreshCoordinator = null)
    {
        _leetCodeService = leetCodeService ?? new LeetCodeService();
        _configService = configService ?? new ConfigurationService();
        _cacheService = cacheService ?? new CacheService();
        _refreshCoordinator = refreshCoordinator ?? new RefreshCoordinator();

        _selectedYear = DateTime.Now.Year;
        _currentActivity = YearlyActivity.Create(_selectedYear);
        _currentYearStats = YearlyStatistics.Empty(_selectedYear);

        PopulateDefaultYears();

        RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
        ToggleEditUsernameCommand = new RelayCommand(_ => IsEditingUsername = !IsEditingUsername);
        SaveUsernameCommand = new RelayCommand(async _ => await SaveUsernameAndRefreshAsync());

        // Connect refresh coordinator to sync callback
        _refreshCoordinator.Start(PerformSyncAsync);
    }

    public async Task InitializeAsync()
    {
        // 1. Load persisted configuration
        var config = await _configService.LoadConfigurationAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(_username) && !string.IsNullOrWhiteSpace(config.Username))
        {
            _username = config.Username;
            OnPropertyChanged(nameof(Username));
        }

        if (config.SelectedYear > 2000)
        {
            _selectedYear = config.SelectedYear;
            OnPropertyChanged(nameof(SelectedYear));
        }

        _refreshCoordinator.Interval = TimeSpan.FromMinutes(config.RefreshIntervalMinutes);

        // 2. Load cached activity immediately
        if (!string.IsNullOrWhiteSpace(_username))
        {
            var cached = await _cacheService.LoadCacheAsync(_username).ConfigureAwait(false);
            if (cached != null)
            {
                _lastSuccessfulStats = cached.ToUserStats();
                PopulateActiveYears(_lastSuccessfulStats.ActiveYears);
                _streaks = StreakCalculationService.CalculateStreaks(
                    _lastSuccessfulStats.SubmissionCounts,
                    ReferenceDate);

                UpdateActivityForSelectedYear();
                NotifyStatsChanged();

                SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                    _lastSuccessfulStats.LastSyncedAt,
                    isOffline: false,
                    referenceTime: ReferenceDateTime);
            }
            else
            {
                SyncStatusText = "Configured · syncing...";
            }
        }
        else
        {
            SyncStatusText = "Configure username to start";
        }

        if (!_refreshCoordinator.IsRunning)
        {
            _refreshCoordinator.Start(PerformSyncAsync);
        }

        if (!string.IsNullOrWhiteSpace(_username))
        {
            _ = _refreshCoordinator.TriggerRefreshAsync(isManual: false);
        }
    }

    private void PopulateDefaultYears()
    {
        int current = DateTime.Now.Year;
        AvailableYears.Clear();
        for (int y = current; y >= current - 4; y--)
        {
            AvailableYears.Add(y);
        }
    }

    private void PopulateActiveYears(IReadOnlyList<int>? years)
    {
        if (years == null || years.Count == 0) return;

        int curYear = SelectedYear;
        AvailableYears.Clear();
        foreach (var y in years.OrderByDescending(x => x))
        {
            AvailableYears.Add(y);
        }
        if (!AvailableYears.Contains(curYear))
        {
            AvailableYears.Insert(0, curYear);
        }
    }

    public async Task<bool> RequestManualRefreshAsync()
    {
        if (_isDisposed) return false;
        return await _refreshCoordinator.TriggerRefreshAsync(isManual: true).ConfigureAwait(false);
    }

    public async Task RefreshAsync()
    {
        await RequestManualRefreshAsync().ConfigureAwait(false);
    }

    public async Task PerformSyncAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed) return;

        if (string.IsNullOrWhiteSpace(Username))
        {
            SyncStatusText = "Configure a username to fetch data";
            return;
        }

        IsSyncing = true;
        SyncStatusText = "Syncing with LeetCode...";

        var result = await _leetCodeService.GetUserActivityAsync(Username, SelectedYear, cancellationToken).ConfigureAwait(false);

        if (_isDisposed) return;
        IsSyncing = false;

        if (result.IsSuccess && result.Data != null)
        {
            _lastSuccessfulStats = result.Data;

            // Persist to local cache atomically
            await _cacheService.SaveCacheAsync(result.Data).ConfigureAwait(false);

            PopulateActiveYears(result.Data.ActiveYears);

            _streaks = StreakCalculationService.CalculateStreaks(
                result.Data.SubmissionCounts,
                ReferenceDate);

            UpdateActivityForSelectedYear();
            NotifyStatsChanged();

            SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                result.Data.LastSyncedAt,
                isOffline: false,
                referenceTime: ReferenceDateTime);
        }
        else
        {
            // Failed sync: preserve previously cached data and indicate offline/stale status
            string err = result.ErrorMessage ?? "Synchronization failed.";
            bool hasCache = _lastSuccessfulStats != null;

            SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                _lastSuccessfulStats?.LastSyncedAt,
                isOffline: true,
                failureReason: err,
                referenceTime: ReferenceDateTime);

            if (!hasCache)
            {
                // Clear to empty calendar if no cache exists
                CurrentActivity = YearlyActivity.Create(SelectedYear, null);
                _currentYearStats = YearlyStatistics.Empty(SelectedYear);
                _streaks = StreakInfo.Empty;
                NotifyStatsChanged();
            }
        }
    }

    private async Task SaveUsernameAndRefreshAsync()
    {
        IsEditingUsername = false;
        var sanitized = WidgetConfig.SanitizeUsername(Username);
        Username = sanitized;

        await SaveConfigAsync().ConfigureAwait(false);

        // Check if there is cache for this new username
        var cached = await _cacheService.LoadCacheAsync(sanitized).ConfigureAwait(false);
        if (cached != null)
        {
            _lastSuccessfulStats = cached.ToUserStats();
            PopulateActiveYears(_lastSuccessfulStats.ActiveYears);
            _streaks = StreakCalculationService.CalculateStreaks(_lastSuccessfulStats.SubmissionCounts, ReferenceDate);
            UpdateActivityForSelectedYear();
            NotifyStatsChanged();
            SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                _lastSuccessfulStats.LastSyncedAt,
                isOffline: false,
                referenceTime: ReferenceDateTime);
        }
        else
        {
            _lastSuccessfulStats = null;
            CurrentActivity = YearlyActivity.Create(SelectedYear, null);
            _currentYearStats = YearlyStatistics.Empty(SelectedYear);
            _streaks = StreakInfo.Empty;
            NotifyStatsChanged();
            SyncStatusText = "Username updated · syncing...";
        }

        await RequestManualRefreshAsync().ConfigureAwait(false);
    }

    private async Task SaveConfigAsync()
    {
        try
        {
            await _configService.SaveConfigurationAsync(new WidgetConfig
            {
                Username = Username,
                SelectedYear = SelectedYear,
                RefreshIntervalMinutes = (int)_refreshCoordinator.Interval.TotalMinutes
            }).ConfigureAwait(false);
        }
        catch
        {
            // Fail safely
        }
    }

    public void UpdateActivityForSelectedYear()
    {
        var submissionMap = _lastSuccessfulStats?.SubmissionCounts;
        CurrentActivity = YearlyActivity.Create(SelectedYear, submissionMap);

        // Recalculate selected-year specific telemetry
        _currentYearStats = StreakCalculationService.CalculateYearlyStatistics(
            SelectedYear,
            submissionMap,
            ReferenceDate);

        OnPropertyChanged(nameof(CurrentYearStats));
        OnPropertyChanged(nameof(YearSubmissions));
        OnPropertyChanged(nameof(YearActiveDays));
        OnPropertyChanged(nameof(YearAverageText));
        OnPropertyChanged(nameof(MostActiveDayText));
        OnPropertyChanged(nameof(YearLongestStreak));
    }

    private void NotifyStatsChanged()
    {
        OnPropertyChanged(nameof(TotalSolved));
        OnPropertyChanged(nameof(EasySolved));
        OnPropertyChanged(nameof(MediumSolved));
        OnPropertyChanged(nameof(HardSolved));
        OnPropertyChanged(nameof(HasSolvedStats));
        OnPropertyChanged(nameof(CurrentStreak));
        OnPropertyChanged(nameof(LongestStreak));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _refreshCoordinator.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
