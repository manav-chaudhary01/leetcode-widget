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
    private readonly IAccountVerificationService _verificationService;

    private string _username = "";
    private string _usernameBeforeEdit = "";
    private bool _isEditingUsername;
    private string _verifiedUsername = "";
    private DateTime? _verifiedAt;
    private bool _isVerificationPromptOpen;
    private bool _isVerifying;
    private string _verificationToken = "";
    private string _verificationStatusText = "";
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
                OnPropertyChanged(nameof(DisplayUsername));
                OnPropertyChanged(nameof(IsAccountVerified));
                OnPropertyChanged(nameof(VerificationBadgeTooltip));
            }
        }
    }

    public string DisplayUsername => string.IsNullOrWhiteSpace(Username) ? "@configure-user" : Username;

    public string VerifiedUsername
    {
        get => _verifiedUsername;
        private set
        {
            if (_verifiedUsername != value)
            {
                _verifiedUsername = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAccountVerified));
                OnPropertyChanged(nameof(VerificationBadgeTooltip));
            }
        }
    }

    public DateTime? VerifiedAt
    {
        get => _verifiedAt;
        private set
        {
            if (_verifiedAt != value)
            {
                _verifiedAt = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VerificationBadgeTooltip));
            }
        }
    }

    public bool IsAccountVerified => !string.IsNullOrWhiteSpace(_verifiedUsername) &&
                                     !string.IsNullOrWhiteSpace(Username) &&
                                     string.Equals(Username, _verifiedUsername, StringComparison.OrdinalIgnoreCase);

    public string VerificationBadgeTooltip => IsAccountVerified
        ? $"Verified ownership of @{Username} ({_verifiedAt?.ToLocalTime():yyyy-MM-dd})"
        : "Account ownership unverified. Click to verify.";

    public bool IsVerificationPromptOpen
    {
        get => _isVerificationPromptOpen;
        set
        {
            if (_isVerificationPromptOpen != value)
            {
                _isVerificationPromptOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsVerifying
    {
        get => _isVerifying;
        private set
        {
            if (_isVerifying != value)
            {
                _isVerifying = value;
                OnPropertyChanged();
            }
        }
    }

    public string VerificationToken
    {
        get => _verificationToken;
        private set
        {
            if (_verificationToken != value)
            {
                _verificationToken = value;
                OnPropertyChanged();
            }
        }
    }

    public string VerificationStatusText
    {
        get => _verificationStatusText;
        private set
        {
            if (_verificationStatusText != value)
            {
                _verificationStatusText = value;
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
                _ = RequestManualRefreshAsync();
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
    public ICommand CancelEditUsernameCommand { get; }
    public ICommand StartVerificationCommand { get; }
    public ICommand CheckVerificationCommand { get; }
    public ICommand CancelVerificationCommand { get; }
    public ICommand CopyVerificationTokenCommand { get; }

    public MainViewModel(
        ILeetCodeService? leetCodeService = null,
        IConfigurationService? configService = null,
        ICacheService? cacheService = null,
        IRefreshCoordinator? refreshCoordinator = null,
        IAccountVerificationService? verificationService = null)
    {
        _leetCodeService = leetCodeService ?? new LeetCodeService();
        _configService = configService ?? new ConfigurationService();
        _cacheService = cacheService ?? new CacheService();
        _refreshCoordinator = refreshCoordinator ?? new RefreshCoordinator();
        _verificationService = verificationService ?? new AccountVerificationService(_leetCodeService);

        _selectedYear = DateTime.Now.Year;
        _currentActivity = YearlyActivity.Create(_selectedYear);
        _currentYearStats = YearlyStatistics.Empty(_selectedYear);

        PopulateDefaultYears();

        RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
        ToggleEditUsernameCommand = new RelayCommand(_ =>
        {
            if (!IsEditingUsername)
            {
                _usernameBeforeEdit = Username;
                IsEditingUsername = true;
            }
            else
            {
                CancelEditUsername();
            }
        });
        SaveUsernameCommand = new RelayCommand(async _ => await SaveUsernameAndRefreshAsync());
        CancelEditUsernameCommand = new RelayCommand(_ => CancelEditUsername());

        StartVerificationCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                SyncStatusText = "Configure a username before verifying ownership.";
                return;
            }

            try
            {
                var challenge = _verificationService.GenerateChallenge(Username);
                VerificationToken = challenge.Token;
                VerificationStatusText = "Paste the token into your LeetCode About Me bio and click 'Verify Now'.";
                IsVerificationPromptOpen = true;
            }
            catch (Exception ex)
            {
                VerificationStatusText = ex.Message;
            }
        });

        CheckVerificationCommand = new RelayCommand(async _ =>
        {
            if (IsVerifying || string.IsNullOrWhiteSpace(Username)) return;

            IsVerifying = true;
            VerificationStatusText = "Checking LeetCode profile 'About Me' field...";

            var result = await _verificationService.VerifyOwnershipAsync(Username).ConfigureAwait(false);

            IsVerifying = false;

            if (result.IsSuccess)
            {
                VerifiedUsername = result.VerifiedUsername ?? Username;
                VerifiedAt = result.VerifiedAtUtc ?? DateTime.UtcNow;
                IsVerificationPromptOpen = false;
                SyncStatusText = "Account verified! You can now remove the token from your profile.";
                await SaveConfigAsync().ConfigureAwait(false);
            }
            else
            {
                VerificationStatusText = result.ErrorMessage ?? "Verification failed.";
            }
        });

        CancelVerificationCommand = new RelayCommand(_ =>
        {
            _verificationService.CancelChallenge();
            IsVerificationPromptOpen = false;
            VerificationToken = "";
            VerificationStatusText = "";
        });

        CopyVerificationTokenCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(VerificationToken))
            {
                try
                {
                    System.Windows.Clipboard.SetText(VerificationToken);
                    VerificationStatusText = "Token copied to clipboard! Paste it into your LeetCode About Me bio.";
                }
                catch
                {
                    VerificationStatusText = $"Token: {VerificationToken}";
                }
            }
        });

        // Connect refresh coordinator to sync callback
        _refreshCoordinator.Start(PerformSyncAsync);
    }

    public void CancelEditUsername()
    {
        Username = _usernameBeforeEdit;
        IsEditingUsername = false;
        if (string.IsNullOrWhiteSpace(Username))
        {
            SyncStatusText = "Click @configure-user to set username";
        }
    }

    public async Task InitializeAsync()
    {
        // 1. Load persisted configuration
        var config = await _configService.LoadConfigurationAsync().ConfigureAwait(false);
        SyncLogger.Log(1, "Configuration loaded", $"Username: {config.Username}, SelectedYear: {config.SelectedYear}, Interval: {config.RefreshIntervalMinutes}m");

        if (string.IsNullOrWhiteSpace(_username) && !string.IsNullOrWhiteSpace(config.Username))
        {
            _username = config.Username;
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(DisplayUsername));
        }

        if (!string.IsNullOrWhiteSpace(config.VerifiedUsername))
        {
            _verifiedUsername = config.VerifiedUsername;
            _verifiedAt = config.VerifiedAt;
            OnPropertyChanged(nameof(VerifiedUsername));
            OnPropertyChanged(nameof(VerifiedAt));
            OnPropertyChanged(nameof(IsAccountVerified));
            OnPropertyChanged(nameof(VerificationBadgeTooltip));
            SyncLogger.Log(2, "Verified username loaded", $"VerifiedUsername: {_verifiedUsername}, VerifiedAt: {_verifiedAt:u}");
        }
        else
        {
            SyncLogger.Log(2, "Verified username loaded", "No verified username configured");
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

                SyncLogger.Log(3, "Cached activity restored",
                    $"Solved: {_lastSuccessfulStats.TotalSolved}, Submissions: {_lastSuccessfulStats.SubmissionCounts.Count} dates, LastSyncedAt: {_lastSuccessfulStats.LastSyncedAt:u}");
            }
            else
            {
                SyncStatusText = "Configured · syncing...";
                SyncLogger.Log(3, "Cached activity restored", "No cached activity found");
            }
        }
        else
        {
            SyncStatusText = "Configure username to start";
            IsEditingUsername = true;
            SyncLogger.Log(3, "Cached activity restored", "Skipped - no username configured");
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

        Action action = () =>
        {
            int curYear = SelectedYear;
            var distinctYears = new HashSet<int>(years) { curYear, DateTime.Now.Year };
            foreach (var y in AvailableYears)
            {
                distinctYears.Add(y);
            }

            var sortedYears = distinctYears.OrderByDescending(x => x).ToList();
            if (AvailableYears.SequenceEqual(sortedYears))
            {
                return;
            }

            AvailableYears.Clear();
            foreach (var y in sortedYears)
            {
                AvailableYears.Add(y);
            }
        };

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else if (dispatcher == null || !dispatcher.HasShutdownStarted)
        {
            action();
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

        SyncLogger.Log(4, "Refresh requested", $"Username: {Username}, Year: {SelectedYear}");

        IsSyncing = true;
        SyncStatusText = "Syncing with LeetCode...";

        try
        {
            var result = await _leetCodeService.GetUserActivityAsync(Username, SelectedYear, cancellationToken).ConfigureAwait(false);

            if (_isDisposed) return;

            if (result.IsSuccess && result.Data != null)
            {
                var data = result.Data;

                // Independent calendar vs solved statistics handling
                var mergedSubmissions = new Dictionary<DateOnly, int>(_lastSuccessfulStats?.SubmissionCounts ?? new Dictionary<DateOnly, int>());

                if (data.HasCalendarData)
                {
                    // Merge new year's submissions into cumulative submissions
                    foreach (var (date, count) in data.SubmissionCounts)
                    {
                        mergedSubmissions[date] = count;
                    }
                }

                // If active years were returned, preserve or update them
                var activeYears = (data.ActiveYears != null && data.ActiveYears.Count > 0)
                    ? data.ActiveYears
                    : (_lastSuccessfulStats?.ActiveYears ?? Array.Empty<int>());

                _lastSuccessfulStats = new LeetCodeUserStats
                {
                    Username = data.Username,
                    TotalSolved = data.TotalSolved,
                    EasySolved = data.EasySolved,
                    MediumSolved = data.MediumSolved,
                    HardSolved = data.HardSolved,
                    CurrentStreak = data.CurrentStreak > 0 ? data.CurrentStreak : (_lastSuccessfulStats?.CurrentStreak ?? 0),
                    TotalActiveDays = data.TotalActiveDays > 0 ? data.TotalActiveDays : (_lastSuccessfulStats?.TotalActiveDays ?? 0),
                    ActiveYears = activeYears,
                    SubmissionCounts = mergedSubmissions,
                    LastSyncedAt = data.LastSyncedAt ?? DateTime.Now,
                    HasCalendarData = data.HasCalendarData,
                    CalendarErrorMessage = data.CalendarErrorMessage
                };

                // Persist to local cache atomically
                await _cacheService.SaveCacheAsync(_lastSuccessfulStats).ConfigureAwait(false);

                PopulateActiveYears(activeYears);

                _streaks = StreakCalculationService.CalculateStreaks(
                    mergedSubmissions,
                    ReferenceDate);

                UpdateActivityForSelectedYear();
                NotifyStatsChanged();

                SyncLogger.Log(15, "MainViewModel updated",
                    $"Year: {SelectedYear}, Solved: {TotalSolved}, YearSubmissions: {YearSubmissions}, YearActiveDays: {YearActiveDays}");

                if (!data.HasCalendarData)
                {
                    SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                        _lastSuccessfulStats.LastSyncedAt,
                        isOffline: false,
                        failureReason: data.CalendarErrorMessage ?? "Calendar data unavailable",
                        referenceTime: ReferenceDateTime);
                }
                else
                {
                    SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                        _lastSuccessfulStats.LastSyncedAt,
                        isOffline: false,
                        referenceTime: ReferenceDateTime);
                }

                SyncLogger.Log(17, "Refresh completed", $"Status: {SyncStatusText}");
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

                SyncLogger.Log(17, "Refresh completed", $"Status: {SyncStatusText}");
            }
        }
        catch (OperationCanceledException)
        {
            SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                _lastSuccessfulStats?.LastSyncedAt,
                isOffline: true,
                failureReason: "Request timed out",
                referenceTime: ReferenceDateTime);
            SyncLogger.Log(17, "Refresh completed", $"Cancelled: {SyncStatusText}");
        }
        catch (Exception ex)
        {
            SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                _lastSuccessfulStats?.LastSyncedAt,
                isOffline: true,
                failureReason: ex.Message,
                referenceTime: ReferenceDateTime);
            SyncLogger.Log(17, "Refresh completed", $"Exception: {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
            SyncLogger.Log(18, "Refresh state and status text reset", $"IsSyncing: {IsSyncing}, Status: {SyncStatusText}");
        }
    }

    private async Task SaveUsernameAndRefreshAsync()
    {
        IsEditingUsername = false;
        var sanitized = WidgetConfig.SanitizeUsername(Username);
        Username = sanitized;

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            SyncStatusText = "Please enter a valid username";
            return;
        }

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
                RefreshIntervalMinutes = (int)_refreshCoordinator.Interval.TotalMinutes,
                VerifiedUsername = _verifiedUsername,
                VerifiedAt = _verifiedAt
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

        SyncLogger.Log(14, "Selected-year statistics calculated",
            $"Year: {SelectedYear}, Submissions: {_currentYearStats.TotalSubmissions}, ActiveDays: {_currentYearStats.ActiveDays}, Streak: {_currentYearStats.LongestStreak}");

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
