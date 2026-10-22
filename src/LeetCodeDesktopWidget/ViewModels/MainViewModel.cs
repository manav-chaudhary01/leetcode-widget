using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using LeetCodeDesktopWidget.Helpers;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;

namespace LeetCodeDesktopWidget.ViewModels;

/// <summary>
/// Presentation logic and state manager connecting real LeetCode data to the desktop widget UI.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly ILeetCodeService _leetCodeService;
    private string _username = "";
    private bool _isEditingUsername;
    private int _selectedYear;
    private YearlyActivity _currentActivity;
    private DailySubmission? _hoveredDay;
    private string _hoverInfoText = "Hover over a square to view submissions";
    private bool _isSyncing;
    private string _syncStatusText = "Not synced";
    private LeetCodeUserStats? _lastSuccessfulStats;

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

    public int TotalSolved => _lastSuccessfulStats?.TotalSolved ?? 0;
    public int EasySolved => _lastSuccessfulStats?.EasySolved ?? 0;
    public int MediumSolved => _lastSuccessfulStats?.MediumSolved ?? 0;
    public int HardSolved => _lastSuccessfulStats?.HardSolved ?? 0;
    public int CurrentStreak => _lastSuccessfulStats?.CurrentStreak ?? 0;
    public int TotalSubmissions => _currentActivity?.TotalSubmissions ?? 0;
    public int ActiveDays => _currentActivity?.ActiveDays ?? 0;

    public string SummaryStatsText => _currentActivity != null
        ? $"{_currentActivity.TotalSubmissions} submissions in {_currentActivity.Year}"
        : "";

    public ICommand RefreshCommand { get; }
    public ICommand ToggleEditUsernameCommand { get; }
    public ICommand SaveUsernameCommand { get; }

    public MainViewModel(ILeetCodeService? leetCodeService = null)
    {
        _leetCodeService = leetCodeService ?? new LeetCodeService();
        _selectedYear = DateTime.Now.Year;
        _currentActivity = YearlyActivity.Create(_selectedYear);

        PopulateDefaultYears();

        RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
        ToggleEditUsernameCommand = new RelayCommand(_ => IsEditingUsername = !IsEditingUsername);
        SaveUsernameCommand = new RelayCommand(async _ =>
        {
            IsEditingUsername = false;
            await RefreshAsync();
        });
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

    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            SyncStatusText = "Configure a username to fetch data";
            return;
        }

        IsSyncing = true;
        SyncStatusText = "Syncing with LeetCode...";

        var result = await _leetCodeService.GetUserActivityAsync(Username, SelectedYear);

        IsSyncing = false;

        if (result.IsSuccess && result.Data != null)
        {
            _lastSuccessfulStats = result.Data;
            SyncStatusText = $"Synced {result.Data.LastSyncedAt:h:mm tt}";

            // Update years if available
            if (result.Data.ActiveYears != null && result.Data.ActiveYears.Count > 0)
            {
                int curYear = SelectedYear;
                AvailableYears.Clear();
                foreach (var y in result.Data.ActiveYears.OrderByDescending(x => x))
                {
                    AvailableYears.Add(y);
                }
                if (!AvailableYears.Contains(curYear))
                {
                    AvailableYears.Insert(0, curYear);
                }
            }

            UpdateActivityForSelectedYear();
            NotifyStatsChanged();
        }
        else
        {
            // Preserve previously cached activity; update status message
            string err = result.ErrorMessage ?? "Synchronization failed.";
            SyncStatusText = _lastSuccessfulStats != null
                ? $"Sync failed ({err}) - showing cached"
                : $"Error: {err}";
        }
    }

    private void UpdateActivityForSelectedYear()
    {
        var submissionMap = _lastSuccessfulStats?.SubmissionCounts;
        CurrentActivity = YearlyActivity.Create(SelectedYear, submissionMap);
    }

    private void NotifyStatsChanged()
    {
        OnPropertyChanged(nameof(TotalSolved));
        OnPropertyChanged(nameof(EasySolved));
        OnPropertyChanged(nameof(MediumSolved));
        OnPropertyChanged(nameof(HardSolved));
        OnPropertyChanged(nameof(CurrentStreak));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
