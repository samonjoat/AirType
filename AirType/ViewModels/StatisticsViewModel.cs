using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AirType.Models;
using AirType.Services.Database;
using AirType.Services.Storage;

namespace AirType.ViewModels;

/// <summary>
/// ViewModel for the Statistics page. Loads live data from DailyStatisticsManager,
/// computes streaks, achievements, and supports period-based filtering.
/// </summary>
public class StatisticsViewModel : BaseViewModel
{
    private readonly DailyStatisticsManager _statsManager;
    private readonly HistoryDatabase _historyDatabase;
    private readonly TranscriptionHistoryManager _historyManager;
    private List<DailyStatistics> _allStats = new();

    // Hero section (period-filtered)
    private string _totalWords = "0";
    private string _timeSavedHours = "0H";
    private string _wordsPerMinute = "0 WPM";
    private string _bookEquivalent = "~0 books written";

    // Stat cards (always all-time)
    private string _daysUsed = "0";
    private string _currentStreak = "0 days";
    private string _bestStreak = "0";
    private string _thisWeekWords = "0";
    private string _avgWordsPerDay = "0";

    // Filter
    private string _selectedPeriod = "All Time";

    // Footer
    private string _memberSince = "";
    private string _wordActivityStartLabel = "";
    private string _wordActivityEndLabel = "";
    private string _wordActivitySummary = "No dictation yet";

    // Achievements
    private bool _achievementFirstWords;
    private bool _achievementTenK;
    private bool _achievementTwentyFiveK;
    private bool _achievementFiftyK;
    private bool _achievementHundredK;
    private string _achievementCount = "0 badges";
    private string _achievementFirstWordsDate = "";
    private string _achievementTenKDate = "";
    private string _achievementTwentyFiveKDate = "";
    private string _achievementFiftyKDate = "";
    private string _achievementHundredKDate = "";

    // Streak badge visibility
    private bool _hasStreak;

    #region Properties

    public string TotalWords
    {
        get => _totalWords;
        set => SetProperty(ref _totalWords, value);
    }

    public string TimeSavedHours
    {
        get => _timeSavedHours;
        set => SetProperty(ref _timeSavedHours, value);
    }

    public string WordsPerMinute
    {
        get => _wordsPerMinute;
        set => SetProperty(ref _wordsPerMinute, value);
    }

    public string BookEquivalent
    {
        get => _bookEquivalent;
        set => SetProperty(ref _bookEquivalent, value);
    }

    public string DaysUsed
    {
        get => _daysUsed;
        set => SetProperty(ref _daysUsed, value);
    }

    public string CurrentStreak
    {
        get => _currentStreak;
        set => SetProperty(ref _currentStreak, value);
    }

    public string BestStreak
    {
        get => _bestStreak;
        set => SetProperty(ref _bestStreak, value);
    }

    public string ThisWeekWords
    {
        get => _thisWeekWords;
        set => SetProperty(ref _thisWeekWords, value);
    }

    public string AvgWordsPerDay
    {
        get => _avgWordsPerDay;
        set => SetProperty(ref _avgWordsPerDay, value);
    }

    public string SelectedPeriod
    {
        get => _selectedPeriod;
        set => SetProperty(ref _selectedPeriod, value);
    }

    public string MemberSince
    {
        get => _memberSince;
        set => SetProperty(ref _memberSince, value);
    }

    public ObservableCollection<int> WordActivityData { get; } = new();

    public string WordActivityStartLabel
    {
        get => _wordActivityStartLabel;
        set => SetProperty(ref _wordActivityStartLabel, value);
    }

    public string WordActivityEndLabel
    {
        get => _wordActivityEndLabel;
        set => SetProperty(ref _wordActivityEndLabel, value);
    }

    public string WordActivitySummary
    {
        get => _wordActivitySummary;
        set => SetProperty(ref _wordActivitySummary, value);
    }

    public bool AchievementFirstWords
    {
        get => _achievementFirstWords;
        set => SetProperty(ref _achievementFirstWords, value);
    }

    public bool AchievementTenK
    {
        get => _achievementTenK;
        set => SetProperty(ref _achievementTenK, value);
    }

    public bool AchievementTwentyFiveK
    {
        get => _achievementTwentyFiveK;
        set => SetProperty(ref _achievementTwentyFiveK, value);
    }

    public bool AchievementFiftyK
    {
        get => _achievementFiftyK;
        set => SetProperty(ref _achievementFiftyK, value);
    }

    public bool AchievementHundredK
    {
        get => _achievementHundredK;
        set => SetProperty(ref _achievementHundredK, value);
    }

    public string AchievementCount
    {
        get => _achievementCount;
        set => SetProperty(ref _achievementCount, value);
    }

    public string AchievementFirstWordsDate
    {
        get => _achievementFirstWordsDate;
        set => SetProperty(ref _achievementFirstWordsDate, value);
    }

    public string AchievementTenKDate
    {
        get => _achievementTenKDate;
        set => SetProperty(ref _achievementTenKDate, value);
    }

    public string AchievementTwentyFiveKDate
    {
        get => _achievementTwentyFiveKDate;
        set => SetProperty(ref _achievementTwentyFiveKDate, value);
    }

    public string AchievementFiftyKDate
    {
        get => _achievementFiftyKDate;
        set => SetProperty(ref _achievementFiftyKDate, value);
    }

    public string AchievementHundredKDate
    {
        get => _achievementHundredKDate;
        set => SetProperty(ref _achievementHundredKDate, value);
    }

    public string AchievementFirstWordsStatus => AchievementFirstWords ? $"Reached {AchievementFirstWordsDate}" : "Locked";
    public string AchievementTenKStatus => AchievementTenK ? $"Reached {AchievementTenKDate}" : "Locked";
    public string AchievementTwentyFiveKStatus => AchievementTwentyFiveK ? $"Reached {AchievementTwentyFiveKDate}" : "Locked";
    public string AchievementFiftyKStatus => AchievementFiftyK ? $"Reached {AchievementFiftyKDate}" : "Locked";
    public string AchievementHundredKStatus => AchievementHundredK ? $"Reached {AchievementHundredKDate}" : "Locked";

    public bool HasStreak
    {
        get => _hasStreak;
        set => SetProperty(ref _hasStreak, value);
    }

    public ICommand SelectPeriodCommand { get; }

    #endregion

    public StatisticsViewModel(
        DailyStatisticsManager statsManager,
        HistoryDatabase historyDatabase,
        TranscriptionHistoryManager historyManager)
    {
        _statsManager = statsManager;
        _historyDatabase = historyDatabase;
        _historyManager = historyManager;
        SelectPeriodCommand = new RelayCommand<string>(FilterByPeriod);

        // Live-refresh when transcription history changes (matches HistoryViewModel pattern)
        _historyManager.HistoryChanged += (s, e) =>
        {
            Application.Current.Dispatcher.Invoke(() => _ = LoadDataAsync());
        };

        _ = LoadDataAsync();
    }

    /// <summary>
    /// Loads all daily statistics from the database. Runs backfill if no data exists.
    /// </summary>
    public async Task LoadDataAsync()
    {
        try
        {
            _allStats = await _statsManager.GetAllStatsAsync();

            // If no cached stats exist, try backfilling from TranscriptionHistory
            if (_allStats.Count == 0)
            {
                await _statsManager.BackfillFromHistoryAsync();
                _allStats = await _statsManager.GetAllStatsAsync();
            }

            ComputeGlobalStats();
            ComputeHeroStats(_allStats);
            ComputeWordActivity();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StatisticsVM] Error loading data: {ex.Message}");
        }
    }

    /// <summary>
    /// Computes stats that are always based on all-time data regardless of period filter:
    /// DaysUsed, Streaks, ThisWeekWords, AvgWordsPerDay, MemberSince, Achievements.
    /// </summary>
    private void ComputeGlobalStats()
    {
        var activeDays = _allStats.Where(s => s.ActiveDay).ToList();
        DaysUsed = activeDays.Count.ToString("N0");

        // Streaks
        var (current, best) = CalculateStreaks(activeDays);
        CurrentStreak = current == 1 ? "1 day" : $"{current} days";
        BestStreak = best.ToString("N0");
        HasStreak = current > 0;

        // This week (last 7 days)
        var weekAgo = DateTime.Today.AddDays(-7);
        var thisWeekTotal = _allStats
            .Where(s => DateTime.TryParse(s.Date, out var d) && d > weekAgo)
            .Sum(s => s.TotalWords);
        ThisWeekWords = thisWeekTotal.ToString("N0");

        // Average words per day
        int totalDays = activeDays.Count;
        int totalWords = _allStats.Sum(s => s.TotalWords);
        AvgWordsPerDay = totalDays > 0 ? (totalWords / totalDays).ToString("N0") : "0";

        // Member since (earliest date in stats)
        var earliest = _allStats.MinBy(s => s.Date);
        if (earliest != null && DateTime.TryParse(earliest.Date, out var memberDate))
        {
            MemberSince = $"Member since {memberDate:MMMM d, yyyy}";
        }

        // Achievements
        ComputeAchievements();
    }

    private void ComputeWordActivity()
    {
        var today = DateTime.Today;
        var start = today.AddDays(-29);
        var wordsByDate = _allStats
            .Select(s => new
            {
                Parsed = DateTime.TryParse(s.Date, out var parsed) ? parsed.Date : (DateTime?)null,
                s.TotalWords
            })
            .Where(s => s.Parsed.HasValue && s.Parsed.Value >= start && s.Parsed.Value <= today)
            .GroupBy(s => s.Parsed!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalWords));

        WordActivityData.Clear();
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            WordActivityData.Add(wordsByDate.TryGetValue(day, out var words) ? words : 0);
        }

        WordActivityStartLabel = start.ToString("MMM d");
        WordActivityEndLabel = today.ToString("MMM d");

        var peak = WordActivityData.Count > 0 ? WordActivityData.Max() : 0;
        var todayWords = WordActivityData.Count > 0 ? WordActivityData[^1] : 0;
        WordActivitySummary = peak > 0
            ? $"peak {peak:N0} · today {todayWords:N0}"
            : "No dictation yet";
    }

    /// <summary>
    /// Computes hero section stats (TotalWords, TimeSaved, WPM, BookEquivalent)
    /// from the given (potentially filtered) stats list.
    /// </summary>
    private void ComputeHeroStats(List<DailyStatistics> stats)
    {
        int words = stats.Sum(s => s.TotalWords);
        double totalSeconds = stats.Sum(s => s.TotalDurationSeconds);

        TotalWords = words.ToString("N0");

        // Time saved — show hours if >= 1h, otherwise minutes
        if (totalSeconds < 60)
            TimeSavedHours = "0M";
        else if (totalSeconds < 3600)
            TimeSavedHours = $"{(int)(totalSeconds / 60)}M";
        else
            TimeSavedHours = $"{(int)(totalSeconds / 3600)}H";

        // Words per minute (avoid division by zero)
        double minutes = totalSeconds / 60.0;
        int wpm = minutes > 0 ? (int)(words / minutes) : 0;
        WordsPerMinute = $"{wpm} WPM";

        // Article equivalent (avg article ≈ 1,250 words)
        int articles = words / 1250;
        BookEquivalent = articles switch
        {
            0 => "~0 articles written",
            1 => "~1 article written",
            _ => $"~{articles} articles written"
        };
    }

    /// <summary>
    /// Calculates current streak (consecutive active days ending today or yesterday)
    /// and best streak (longest consecutive active day run ever).
    /// </summary>
    private static (int current, int best) CalculateStreaks(List<DailyStatistics> activeDays)
    {
        if (activeDays.Count == 0) return (0, 0);

        // Parse and deduplicate dates
        var dates = activeDays
            .Select(s => DateTime.TryParse(s.Date, out var d) ? d.Date : (DateTime?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        if (dates.Count == 0) return (0, 0);

        // Current streak: must start from today or yesterday
        int currentStreak = 0;
        var today = DateTime.Today;
        var startDate = dates[0];

        if (startDate == today || startDate == today.AddDays(-1))
        {
            // Walk backward from the most recent active day
            for (int i = 0; i < dates.Count; i++)
            {
                if (dates[i] == startDate.AddDays(-i))
                    currentStreak++;
                else
                    break;
            }
        }

        // Best streak: scan all dates in ascending order for longest consecutive run
        var ascDates = dates.OrderBy(d => d).ToList();
        int bestStreak = 1;
        int runLength = 1;

        for (int i = 1; i < ascDates.Count; i++)
        {
            if (ascDates[i] == ascDates[i - 1].AddDays(1))
            {
                runLength++;
                if (runLength > bestStreak)
                    bestStreak = runLength;
            }
            else
            {
                runLength = 1;
            }
        }

        return (currentStreak, bestStreak);
    }

    /// <summary>
    /// Computes achievement thresholds by walking cumulative word totals chronologically.
    /// Records the date each threshold was first crossed.
    /// </summary>
    private void ComputeAchievements()
    {
        var sorted = _allStats.OrderBy(s => s.Date).ToList();

        int cumulative = 0;
        bool firstReached = false, tenKReached = false, twentyFiveKReached = false;
        bool fiftyKReached = false, hundredKReached = false;

        foreach (var stat in sorted)
        {
            cumulative += stat.TotalWords;

            if (!firstReached && cumulative >= 1_000)
            {
                firstReached = true;
                if (DateTime.TryParse(stat.Date, out var d))
                    AchievementFirstWordsDate = d.ToString("MMM d");
            }
            if (!tenKReached && cumulative >= 10_000)
            {
                tenKReached = true;
                if (DateTime.TryParse(stat.Date, out var d))
                    AchievementTenKDate = d.ToString("MMM d");
            }
            if (!twentyFiveKReached && cumulative >= 25_000)
            {
                twentyFiveKReached = true;
                if (DateTime.TryParse(stat.Date, out var d))
                    AchievementTwentyFiveKDate = d.ToString("MMM d");
            }
            if (!fiftyKReached && cumulative >= 50_000)
            {
                fiftyKReached = true;
                if (DateTime.TryParse(stat.Date, out var d))
                    AchievementFiftyKDate = d.ToString("MMM d");
            }
            if (!hundredKReached && cumulative >= 100_000)
            {
                hundredKReached = true;
                if (DateTime.TryParse(stat.Date, out var d))
                    AchievementHundredKDate = d.ToString("MMM d");
            }
        }

        AchievementFirstWords = firstReached;
        AchievementTenK = tenKReached;
        AchievementTwentyFiveK = twentyFiveKReached;
        AchievementFiftyK = fiftyKReached;
        AchievementHundredK = hundredKReached;

        int count = (firstReached ? 1 : 0) + (tenKReached ? 1 : 0) + (twentyFiveKReached ? 1 : 0)
                  + (fiftyKReached ? 1 : 0) + (hundredKReached ? 1 : 0);
        AchievementCount = count == 1 ? "1 badge" : $"{count} badges";
        OnPropertyChanged(nameof(AchievementFirstWordsStatus));
        OnPropertyChanged(nameof(AchievementTenKStatus));
        OnPropertyChanged(nameof(AchievementTwentyFiveKStatus));
        OnPropertyChanged(nameof(AchievementFiftyKStatus));
        OnPropertyChanged(nameof(AchievementHundredKStatus));
    }

    /// <summary>
    /// Filters hero section stats by the selected date period.
    /// Called by SelectPeriodCommand when a radio button is clicked.
    /// </summary>
    private void FilterByPeriod(string period)
    {
        SelectedPeriod = period;

        var today = DateTime.Today;
        List<DailyStatistics> filtered;

        switch (period)
        {
            case "Today":
                var todayStr = today.ToString("yyyy-MM-dd");
                filtered = _allStats.Where(s => s.Date == todayStr).ToList();
                break;
            case "This Week":
                var weekAgo = today.AddDays(-7);
                filtered = _allStats
                    .Where(s => DateTime.TryParse(s.Date, out var d) && d > weekAgo)
                    .ToList();
                break;
            case "This Month":
                var monthStart = new DateTime(today.Year, today.Month, 1);
                filtered = _allStats
                    .Where(s => DateTime.TryParse(s.Date, out var d) && d >= monthStart)
                    .ToList();
                break;
            default: // "All Time"
                filtered = _allStats;
                break;
        }

        ComputeHeroStats(filtered);
    }
}
