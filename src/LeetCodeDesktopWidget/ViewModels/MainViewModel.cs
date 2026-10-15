using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;

namespace LeetCodeDesktopWidget.ViewModels;

/// <summary>
/// Main presentation logic and state manager for the desktop widget.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private int _selectedYear;
    private YearlyActivity _currentActivity = null!;
    private DailySubmission? _hoveredDay;
    private string _hoverInfoText = "Hover over a square to view submissions";

    public ObservableCollection<int> AvailableYears { get; } = new()
    {
        2026, 2025, 2024, 2023, 2022
    };

    public int SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (_selectedYear != value)
            {
                _selectedYear = value;
                OnPropertyChanged();
                LoadYearData(_selectedYear);
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

    public int TotalSubmissions => _currentActivity?.TotalSubmissions ?? 0;
    public int ActiveDays => _currentActivity?.ActiveDays ?? 0;
    public string SummaryStatsText => _currentActivity != null
        ? $"{_currentActivity.TotalSubmissions} submissions in {_currentActivity.Year}"
        : "";

    public MainViewModel()
    {
        _selectedYear = 2026;
        LoadYearData(_selectedYear);
    }

    public void LoadYearData(int year)
    {
        // For Milestone 2: populate with realistic mock activity
        CurrentActivity = MockDataService.GenerateMockYearlyActivity(year);
        HoveredDay = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
