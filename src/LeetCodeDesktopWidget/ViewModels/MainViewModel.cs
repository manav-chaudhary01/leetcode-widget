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

public enum WidgetState
{
    Unconfigured,
    VerificationRequired,
    VerificationInProgress,
    VerifiedSyncing,
    Ready,
    Offline
}

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

    private WidgetState _state = WidgetState.Unconfigured;
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
    private YearlyActivity? _currentActivity;
    private DailySubmission? _hoveredDay;
    private string _hoverInfoText = "Hover over a square to view submissions";
    private bool _isSyncing;
    private string _syncStatusText = "Not synced";
    private LeetCodeUserStats? _lastSuccessfulStats;
    private StreakInfo _streaks = StreakInfo.Empty;
    private YearlyStatistics _currentYearStats;
    private bool _isDisposed;
    private DateOnly? _customReferenceDate;
    private double? _windowX;
    private double? _windowY;

    public double? WindowX
    {
        get => _windowX;
        set
        {
            if (_windowX != value)
            {
                _windowX = value;
                OnPropertyChanged();
            }
        }
    }

    public double? WindowY
    {
        get => _windowY;
        set
        {
            if (_windowY != value)
            {
                _windowY = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? ReferenceDateTime { get; set; }

    public DateOnly? ReferenceDate
    {
        get => ReferenceDateTime.HasValue ? DateOnly.FromDateTime(ReferenceDateTime.Value) : _customReferenceDate;
        set => _customReferenceDate = value;
    }

    public ObservableCollection<int> AvailableYears { get; } = new();

    public bool HasAvailableYears => AvailableYears.Count > 0;

    public WidgetState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsUnconfigured));
                OnPropertyChanged(nameof(IsVerificationRequired));
                OnPropertyChanged(nameof(IsVerificationInProgress));
                OnPropertyChanged(nameof(IsVerifiedSyncing));
                OnPropertyChanged(nameof(IsReady));
                OnPropertyChanged(nameof(IsOffline));
                OnPropertyChanged(nameof(CanDisplayActivity));
            }
        }
    }

    public bool IsUnconfigured => State == WidgetState.Unconfigured;
    public bool IsVerificationRequired => State == WidgetState.VerificationRequired;
    public bool IsVerificationInProgress => State == WidgetState.VerificationInProgress;
    public bool IsVerifiedSyncing => State == WidgetState.VerifiedSyncing;
    public bool IsReady => State == WidgetState.Ready;
    public bool IsOffline => State == WidgetState.Offline;

    public string? ActivityDataUsername => _lastSuccessfulStats?.Username;

    public bool HasValidActivityForCurrentAccount =>
        IsAccountVerified &&
        !string.IsNullOrWhiteSpace(ActivityDataUsername) &&
        string.Equals(ActivityDataUsername, VerifiedUsername, StringComparison.OrdinalIgnoreCase);

    public bool CanDisplayActivity => HasValidActivityForCurrentAccount;

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
                OnPropertyChanged(nameof(CanDisplayActivity));
                OnPropertyChanged(nameof(HasValidActivityForCurrentAccount));
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
                OnPropertyChanged(nameof(CanDisplayActivity));
                OnPropertyChanged(nameof(HasValidActivityForCurrentAccount));
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
                if (IsAccountVerified)
                {
                    _ = RequestManualRefreshAsync();
                }
            }
        }
    }

    public YearlyActivity? CurrentActivity
    {
        get => HasValidActivityForCurrentAccount ? _currentActivity : null;
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

    // All-time Solved Counts (Gated strictly by verification and account identity)
    public int TotalSolved => HasValidActivityForCurrentAccount ? (_lastSuccessfulStats?.TotalSolved ?? 0) : 0;
    public int EasySolved => HasValidActivityForCurrentAccount ? (_lastSuccessfulStats?.EasySolved ?? 0) : 0;
    public int MediumSolved => HasValidActivityForCurrentAccount ? (_lastSuccessfulStats?.MediumSolved ?? 0) : 0;
    public int HardSolved => HasValidActivityForCurrentAccount ? (_lastSuccessfulStats?.HardSolved ?? 0) : 0;
    public bool HasSolvedStats => HasValidActivityForCurrentAccount && _lastSuccessfulStats != null;

    // All-time Calculated Streaks (computed purely from daily submissions)
    public int CurrentStreak => HasValidActivityForCurrentAccount ? _streaks.CurrentStreak : 0;
    public int LongestStreak => HasValidActivityForCurrentAccount ? _streaks.LongestStreak : 0;

    // Selected-Year Statistics
    public YearlyStatistics CurrentYearStats => HasValidActivityForCurrentAccount ? _currentYearStats : YearlyStatistics.Empty(SelectedYear);
    public int YearSubmissions => HasValidActivityForCurrentAccount ? _currentYearStats.TotalSubmissions : 0;
    public int YearActiveDays => HasValidActivityForCurrentAccount ? _currentYearStats.ActiveDays : 0;
    public string YearAverageText => (HasValidActivityForCurrentAccount && _currentYearStats.ActiveDays > 0)
        ? $"{_currentYearStats.AveragePerActiveDay:F1}"
        : "0";
    public string MostActiveDayText => HasValidActivityForCurrentAccount
        ? (_currentYearStats.MostActiveDay?.ToString() ?? "None")
        : "None";
    public int YearLongestStreak => HasValidActivityForCurrentAccount ? _currentYearStats.LongestStreak : 0;

    public int TotalSubmissions => HasValidActivityForCurrentAccount ? (_currentActivity?.TotalSubmissions ?? 0) : 0;
    public int ActiveDays => HasValidActivityForCurrentAccount ? (_currentActivity?.ActiveDays ?? 0) : 0;

    public string SummaryStatsText => (HasValidActivityForCurrentAccount && _currentActivity != null)
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
    public ICommand OpenLeetCodeProfileCommand { get; }

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
        _currentYearStats = YearlyStatistics.Empty(_selectedYear);
        _state = WidgetState.Unconfigured;

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
                VerificationStatusText = "Please enter a username before verifying ownership.";
                State = WidgetState.Unconfigured;
                return;
            }

            try
            {
                var challenge = _verificationService.GenerateChallenge(Username);
                VerificationToken = challenge.Token;
                VerificationStatusText = "Paste the token into your LeetCode About Me bio and click 'Verify Ownership'.";
                State = WidgetState.VerificationInProgress;
                IsVerificationPromptOpen = true;
            }
            catch (Exception ex)
            {
                VerificationStatusText = ex.Message;
                State = WidgetState.VerificationRequired;
            }
        });

        CheckVerificationCommand = new RelayCommand(async _ =>
        {
            if (IsVerifying || string.IsNullOrWhiteSpace(Username)) return;

            IsVerifying = true;
            VerificationStatusText = "Checking LeetCode profile 'About Me' field...";
            State = WidgetState.VerificationInProgress;

            var result = await _verificationService.VerifyOwnershipAsync(Username).ConfigureAwait(false);

            IsVerifying = false;

            if (result.IsSuccess)
            {
                VerifiedUsername = result.VerifiedUsername ?? Username;
                VerifiedAt = result.VerifiedAtUtc ?? DateTime.UtcNow;
                IsVerificationPromptOpen = false;
                VerificationToken = "";
                VerificationStatusText = "";
                SyncStatusText = "Account verified! Syncing...";
                State = WidgetState.VerifiedSyncing;

                await SaveConfigAsync().ConfigureAwait(false);

                // Restore cache for this newly verified user if available
                var cached = await _cacheService.LoadCacheAsync(VerifiedUsername).ConfigureAwait(false);
                if (cached != null)
                {
                    var cleanCached = WidgetConfig.SanitizeUsername(cached.Username);
                    var cleanVerified = WidgetConfig.SanitizeUsername(VerifiedUsername);
                    if (string.Equals(cleanCached, cleanVerified, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastSuccessfulStats = cached.ToUserStats();
                        PopulateActiveYears(_lastSuccessfulStats.ActiveYears);
                        _streaks = StreakCalculationService.CalculateStreaks(
                            _lastSuccessfulStats.SubmissionCounts,
                            ReferenceDate);
                        UpdateActivityForSelectedYear();
                        NotifyStatsChanged();
                    }
                }

                await RequestManualRefreshAsync().ConfigureAwait(false);
            }
            else
            {
                State = WidgetState.VerificationRequired;
                VerificationStatusText = result.ErrorMessage ?? "Verification failed.";
            }
        });

        CancelVerificationCommand = new RelayCommand(_ =>
        {
            _verificationService.CancelChallenge();
            IsVerificationPromptOpen = false;
            VerificationToken = "";
            VerificationStatusText = "";
            State = string.IsNullOrWhiteSpace(Username) 
                ? WidgetState.Unconfigured 
                : (IsAccountVerified ? WidgetState.Ready : WidgetState.VerificationRequired);
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

        OpenLeetCodeProfileCommand = new RelayCommand(_ =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://leetcode.com/profile",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
                // Fail safely if default browser cannot be launched
            }
        });

        // Connect refresh coordinator to sync callback
        _refreshCoordinator.Start(PerformSyncAsync);
    }

    public void CancelEditUsername()
    {
        Username = _usernameBeforeEdit;
        IsEditingUsername = false;
        if (!IsAccountVerified)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                State = WidgetState.Unconfigured;
                SyncStatusText = "Click @configure-user to start";
            }
            else
            {
                State = WidgetState.VerificationRequired;
                SyncStatusText = "Account verification required";
            }
        }
    }

    public void SetVerifiedForTesting(string username, DateTime? verifiedAt = null, LeetCodeUserStats? stats = null)
    {
        var sanitized = WidgetConfig.SanitizeUsername(username);
        _username = sanitized;
        _usernameBeforeEdit = sanitized;
        _verifiedUsername = sanitized;
        _verifiedAt = verifiedAt ?? DateTime.UtcNow;
        if (stats != null)
        {
            _lastSuccessfulStats = stats;
            PopulateActiveYears(stats.ActiveYears);
            _streaks = StreakCalculationService.CalculateStreaks(stats.SubmissionCounts, ReferenceDate);
        }
        else if (_lastSuccessfulStats == null)
        {
            _lastSuccessfulStats = new LeetCodeUserStats
            {
                Username = sanitized,
                ActiveYears = new List<int> { SelectedYear },
                LastSyncedAt = DateTime.UtcNow
            };
            PopulateActiveYears(_lastSuccessfulStats.ActiveYears);
        }
        UpdateActivityForSelectedYear();
        NotifyStatsChanged();
        State = WidgetState.Ready;
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(DisplayUsername));
        OnPropertyChanged(nameof(VerifiedUsername));
        OnPropertyChanged(nameof(VerifiedAt));
        OnPropertyChanged(nameof(IsAccountVerified));
        OnPropertyChanged(nameof(VerificationBadgeTooltip));
        OnPropertyChanged(nameof(CanDisplayActivity));
        OnPropertyChanged(nameof(HasValidActivityForCurrentAccount));
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

        if (!string.IsNullOrWhiteSpace(config.VerifiedUsername) &&
            !string.IsNullOrWhiteSpace(config.Username) &&
            string.Equals(config.Username, config.VerifiedUsername, StringComparison.OrdinalIgnoreCase))
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
            _verifiedUsername = "";
            _verifiedAt = null;
            OnPropertyChanged(nameof(VerifiedUsername));
            OnPropertyChanged(nameof(VerifiedAt));
            OnPropertyChanged(nameof(IsAccountVerified));
            OnPropertyChanged(nameof(VerificationBadgeTooltip));
            SyncLogger.Log(2, "Verified username loaded", "No verified username configured or username mismatch");
        }

        if (config.SelectedYear > 2000)
        {
            _selectedYear = config.SelectedYear;
            OnPropertyChanged(nameof(SelectedYear));
        }

        _windowX = config.WindowX;
        _windowY = config.WindowY;
        OnPropertyChanged(nameof(WindowX));
        OnPropertyChanged(nameof(WindowY));

        _refreshCoordinator.Interval = TimeSpan.FromMinutes(config.RefreshIntervalMinutes);

        // 2. Load cached activity immediately ONLY IF verified
        if (IsAccountVerified && !string.IsNullOrWhiteSpace(VerifiedUsername))
        {
            var cached = await _cacheService.LoadCacheAsync(VerifiedUsername).ConfigureAwait(false);
            if (cached != null)
            {
                var cleanCachedUser = WidgetConfig.SanitizeUsername(cached.Username);
                var cleanVerifiedUser = WidgetConfig.SanitizeUsername(VerifiedUsername);

                if (string.Equals(cleanCachedUser, cleanVerifiedUser, StringComparison.OrdinalIgnoreCase))
                {
                    _lastSuccessfulStats = cached.ToUserStats();
                    PopulateActiveYears(_lastSuccessfulStats.ActiveYears);
                    _streaks = StreakCalculationService.CalculateStreaks(
                        _lastSuccessfulStats.SubmissionCounts,
                        ReferenceDate);

                    UpdateActivityForSelectedYear();
                    NotifyStatsChanged();

                    string freshness = FreshnessFormatter.FormatSyncStatus(
                        _lastSuccessfulStats.LastSyncedAt,
                        isOffline: false,
                        referenceTime: ReferenceDateTime);
                    SyncStatusText = IsAccountVerified ? $"Verified · {freshness}" : freshness;

                    State = WidgetState.Ready;

                    SyncLogger.Log(3, "Cached activity restored",
                        $"Solved: {_lastSuccessfulStats.TotalSolved}, Submissions: {_lastSuccessfulStats.SubmissionCounts.Count} dates, LastSyncedAt: {_lastSuccessfulStats.LastSyncedAt:u}");
                }
            }
            else
            {
                SyncStatusText = "Verified · syncing...";
                State = WidgetState.VerifiedSyncing;
                SyncLogger.Log(3, "Cached activity restored", "No cached activity found");
            }
        }
        else
        {
            // Unverified accounts MUST NEVER load or expose cache
            _lastSuccessfulStats = null;
            _currentActivity = null;
            _currentYearStats = YearlyStatistics.Empty(SelectedYear);
            _streaks = StreakInfo.Empty;
            AvailableYears.Clear();
            NotifyStatsChanged();

            if (string.IsNullOrWhiteSpace(Username))
            {
                State = WidgetState.Unconfigured;
                SyncStatusText = "Configure username to start";
                IsEditingUsername = true;
            }
            else
            {
                State = WidgetState.VerificationRequired;
                SyncStatusText = "Account verification required";
            }

            SyncLogger.Log(3, "Cached activity restored", "Skipped - unverified account");
        }

        if (!_refreshCoordinator.IsRunning)
        {
            _refreshCoordinator.Start(PerformSyncAsync);
        }

        if (IsAccountVerified && !string.IsNullOrWhiteSpace(Username))
        {
            _ = _refreshCoordinator.TriggerRefreshAsync(isManual: false);
        }
    }

    public void PopulateActiveYears(IReadOnlyList<int>? activeYears)
    {
        Action action = () =>
        {
            if (activeYears == null || activeYears.Count == 0)
            {
                AvailableYears.Clear();
                OnPropertyChanged(nameof(HasAvailableYears));
                return;
            }

            // Strictly use the account's actual activeYears, sorted descending.
            // Do not invent missing years or default ranges.
            var sortedYears = activeYears
                .Distinct()
                .OrderByDescending(x => x)
                .ToList();

            if (!AvailableYears.SequenceEqual(sortedYears))
            {
                AvailableYears.Clear();
                foreach (var y in sortedYears)
                {
                    AvailableYears.Add(y);
                }
            }

            OnPropertyChanged(nameof(HasAvailableYears));

            // Ensure SelectedYear is one of AvailableYears
            if (sortedYears.Count > 0 && !sortedYears.Contains(SelectedYear))
            {
                SelectedYear = sortedYears[0];
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

        // Invariant: sync is only allowed for verified accounts
        if (!IsAccountVerified || string.IsNullOrWhiteSpace(VerifiedUsername) || !string.Equals(Username, VerifiedUsername, StringComparison.OrdinalIgnoreCase))
        {
            SyncStatusText = "Account verification required";
            State = string.IsNullOrWhiteSpace(Username) ? WidgetState.Unconfigured : WidgetState.VerificationRequired;
            return;
        }

        SyncLogger.Log(4, "Refresh requested", $"Username: {Username}, Year: {SelectedYear}");

        IsSyncing = true;
        State = WidgetState.VerifiedSyncing;
        SyncStatusText = "Syncing with LeetCode...";

        try
        {
            var result = await _leetCodeService.GetUserActivityAsync(Username, SelectedYear, cancellationToken).ConfigureAwait(false);

            if (_isDisposed) return;

            // Double check account was not switched while request was in flight
            if (!IsAccountVerified || !string.Equals(Username, VerifiedUsername, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (result.IsSuccess && result.Data != null)
            {
                var data = result.Data;

                // Ensure data belongs to current verified user
                if (!string.Equals(data.Username, VerifiedUsername, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Independent calendar vs solved statistics handling
                var mergedSubmissions = new Dictionary<DateOnly, int>(
                    HasValidActivityForCurrentAccount ? (_lastSuccessfulStats?.SubmissionCounts ?? new Dictionary<DateOnly, int>()) : new Dictionary<DateOnly, int>());

                if (data.HasCalendarData)
                {
                    foreach (var (date, count) in data.SubmissionCounts)
                    {
                        mergedSubmissions[date] = count;
                    }
                }

                var activeYears = (data.ActiveYears != null && data.ActiveYears.Count > 0)
                    ? data.ActiveYears
                    : (HasValidActivityForCurrentAccount ? (_lastSuccessfulStats?.ActiveYears ?? Array.Empty<int>()) : Array.Empty<int>());

                _lastSuccessfulStats = new LeetCodeUserStats
                {
                    Username = data.Username,
                    TotalSolved = data.TotalSolved,
                    EasySolved = data.EasySolved,
                    MediumSolved = data.MediumSolved,
                    HardSolved = data.HardSolved,
                    CurrentStreak = data.CurrentStreak > 0 ? data.CurrentStreak : (HasValidActivityForCurrentAccount ? (_lastSuccessfulStats?.CurrentStreak ?? 0) : 0),
                    TotalActiveDays = data.TotalActiveDays > 0 ? data.TotalActiveDays : (HasValidActivityForCurrentAccount ? (_lastSuccessfulStats?.TotalActiveDays ?? 0) : 0),
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

                State = WidgetState.Ready;

                SyncLogger.Log(15, "MainViewModel updated",
                    $"Year: {SelectedYear}, Solved: {TotalSolved}, YearSubmissions: {YearSubmissions}, YearActiveDays: {YearActiveDays}");

                if (!data.HasCalendarData)
                {
                    string freshness = FreshnessFormatter.FormatSyncStatus(
                        _lastSuccessfulStats.LastSyncedAt,
                        isOffline: false,
                        failureReason: data.CalendarErrorMessage ?? "Calendar data unavailable",
                        referenceTime: ReferenceDateTime);
                    SyncStatusText = IsAccountVerified ? $"Verified · {freshness}" : freshness;
                }
                else
                {
                    string freshness = FreshnessFormatter.FormatSyncStatus(
                        _lastSuccessfulStats.LastSyncedAt,
                        isOffline: false,
                        referenceTime: ReferenceDateTime);
                    SyncStatusText = IsAccountVerified ? $"Verified · {freshness}" : freshness;
                }

                SyncLogger.Log(17, "Refresh completed", $"Status: {SyncStatusText}");
            }
            else
            {
                // Failed sync: only preserve cache if it genuinely belongs to the current verified user
                string err = result.ErrorMessage ?? "Synchronization failed.";
                bool hasCache = HasValidActivityForCurrentAccount;

                if (!hasCache)
                {
                    _lastSuccessfulStats = null;
                    _currentActivity = null;
                    _currentYearStats = YearlyStatistics.Empty(SelectedYear);
                    _streaks = StreakInfo.Empty;
                    AvailableYears.Clear();
                    NotifyStatsChanged();
                }

                State = hasCache ? WidgetState.Offline : WidgetState.Ready;

                SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                    hasCache ? _lastSuccessfulStats?.LastSyncedAt : null,
                    isOffline: true,
                    failureReason: err,
                    referenceTime: ReferenceDateTime);

                SyncLogger.Log(17, "Refresh completed", $"Status: {SyncStatusText}");
            }
        }
        catch (OperationCanceledException)
        {
            bool hasCache = HasValidActivityForCurrentAccount;
            State = hasCache ? WidgetState.Offline : WidgetState.Ready;
            SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                hasCache ? _lastSuccessfulStats?.LastSyncedAt : null,
                isOffline: true,
                failureReason: "Request timed out",
                referenceTime: ReferenceDateTime);
            SyncLogger.Log(17, "Refresh completed", $"Cancelled: {SyncStatusText}");
        }
        catch (Exception ex)
        {
            bool hasCache = HasValidActivityForCurrentAccount;
            State = hasCache ? WidgetState.Offline : WidgetState.Ready;
            SyncStatusText = FreshnessFormatter.FormatSyncStatus(
                hasCache ? _lastSuccessfulStats?.LastSyncedAt : null,
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

    public async Task SaveUsernameAndRefreshAsync()
    {
        IsEditingUsername = false;
        var sanitized = WidgetConfig.SanitizeUsername(Username);
        string oldUsername = _usernameBeforeEdit;
        Username = sanitized;

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            SyncStatusText = "Please enter a valid username";
            State = WidgetState.Unconfigured;
            return;
        }

        bool isAccountSwitched = !string.Equals(oldUsername, sanitized, StringComparison.OrdinalIgnoreCase);

        if (isAccountSwitched)
        {
            // HARD ACCOUNT ISOLATION:
            // 1. Invalidate old account verification
            VerifiedUsername = "";
            VerifiedAt = null;

            // 2. Clear all in-memory stats, activity, and years from old account
            _lastSuccessfulStats = null;
            _currentActivity = null;
            _currentYearStats = YearlyStatistics.Empty(SelectedYear);
            _streaks = StreakInfo.Empty;
            AvailableYears.Clear();

            UpdateActivityForSelectedYear();
            NotifyStatsChanged();

            // 3. Persist unverified state to configuration
            await SaveConfigAsync().ConfigureAwait(false);

            // 4. Transition to VerificationRequired
            State = WidgetState.VerificationRequired;
            SyncStatusText = "Account verification required";

            // 5. Automatically open verification challenge for the new account
            StartVerificationCommand.Execute(null);
            return;
        }

        if (IsAccountVerified)
        {
            await RequestManualRefreshAsync().ConfigureAwait(false);
        }
        else
        {
            StartVerificationCommand.Execute(null);
        }
    }

    public async Task SaveWindowPositionAsync(double x, double y)
    {
        _windowX = x;
        _windowY = y;
        OnPropertyChanged(nameof(WindowX));
        OnPropertyChanged(nameof(WindowY));
        await SaveConfigAsync().ConfigureAwait(false);
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
                VerifiedAt = _verifiedAt,
                WindowX = _windowX,
                WindowY = _windowY
            }).ConfigureAwait(false);
        }
        catch
        {
            // Fail safely
        }
    }

    public void UpdateActivityForSelectedYear()
    {
        if (!HasValidActivityForCurrentAccount)
        {
            CurrentActivity = null;
            _currentYearStats = YearlyStatistics.Empty(SelectedYear);
            return;
        }

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
        OnPropertyChanged(nameof(YearSubmissions));
        OnPropertyChanged(nameof(YearActiveDays));
        OnPropertyChanged(nameof(YearAverageText));
        OnPropertyChanged(nameof(MostActiveDayText));
        OnPropertyChanged(nameof(YearLongestStreak));
        OnPropertyChanged(nameof(CurrentActivity));
        OnPropertyChanged(nameof(TotalSubmissions));
        OnPropertyChanged(nameof(ActiveDays));
        OnPropertyChanged(nameof(SummaryStatsText));
        OnPropertyChanged(nameof(CanDisplayActivity));
        OnPropertyChanged(nameof(HasValidActivityForCurrentAccount));
        OnPropertyChanged(nameof(ActivityDataUsername));
        OnPropertyChanged(nameof(HasAvailableYears));
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
