using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AirType.Models;
using AirType.Services.Database;
using AirType.Models.Configuration;
using AirType.Services;
using AirType.Services.Dictionary;
using AirType.Services.Storage;
using AirType.Services.Transcription;
using AirType.Views;
using NAudio.Wave;

namespace AirType.ViewModels;

public class HistoryViewModel : BaseViewModel
{
    private readonly TranscriptionHistoryManager _historyManager;
    private readonly ITranscriptionWorkflowService _workflowService;
    private readonly IDictionaryManager _dictionaryManager;
    private readonly ITextDiffService _textDiffService;
    private string _searchQuery = string.Empty;
    private ObservableCollection<TranscriptionHistoryEntry> _filteredEntries;
    private int _renderedCount;
    private int _loadedCount;
    private int _totalCountInDb;
    private string? _lastDateKey;
    private bool _isLoading;
    private string _currentDayInCatcher = string.Empty;
    private Guid? _editingId;
    private string _editingText = string.Empty;
    private const int BatchSize = 10;
    private List<TranscriptionHistoryEntry>? _cachedSortedEntries;
    private bool _disposed;

    public ICommand CopyEntryCommand { get; }
    public ICommand DeleteEntryCommand { get; }
    public ICommand RerunCommand { get; }
    public ICommand DownloadAudioCommand { get; }
    public ICommand ToggleTranscriptDisplayCommand { get; }
    public ICommand StartEditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }

    public Guid? EditingId
    {
        get => _editingId;
        set => SetProperty(ref _editingId, value);
    }

    public string EditingText
    {
        get => _editingText;
        set => SetProperty(ref _editingText, value);
    }

    public string CurrentDayInCatcher
    {
        get => _currentDayInCatcher;
        set
        {
            if (_currentDayInCatcher != value)
            {
                _currentDayInCatcher = value;
                OnPropertyChanged(nameof(CurrentDayInCatcher));
            }
        }
    }

    public HistoryViewModel(
        TranscriptionHistoryManager historyManager, 
        ITranscriptionWorkflowService workflowService,
        IDictionaryManager dictionaryManager,
        ITextDiffService textDiffService)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _dictionaryManager = dictionaryManager ?? throw new ArgumentNullException(nameof(dictionaryManager));
        _textDiffService = textDiffService ?? throw new ArgumentNullException(nameof(textDiffService));
        _filteredEntries = new ObservableCollection<TranscriptionHistoryEntry>();

        CopyEntryCommand = new RelayCommand<TranscriptionHistoryEntry>(ExecuteCopyEntry);
        DeleteEntryCommand = new RelayCommand<TranscriptionHistoryEntry>(ExecuteDeleteEntry);
        RerunCommand = new RelayCommand<TranscriptionHistoryEntry>(async (entry) => await ExecuteRerunAsync(entry));
        DownloadAudioCommand = new RelayCommand<TranscriptionHistoryEntry>(ExecuteDownloadAudio);
        ToggleTranscriptDisplayCommand = new RelayCommand<TranscriptionHistoryEntry>(async (entry) => await ExecuteToggleTranscriptDisplayAsync(entry));
        StartEditCommand = new RelayCommand<TranscriptionHistoryEntry>(ExecuteStartEdit);
        SaveEditCommand = new RelayCommand<TranscriptionHistoryEntry>(async (entry) => await ExecuteSaveEditAsync(entry));
        CancelEditCommand = new RelayCommand(ExecuteCancelEdit);
        
        _historyManager.HistoryChanged += OnHistoryChanged;

        // Defer initial load to not block UI
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed)
            {
                Refresh();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(Refresh);
    }

    private void ExecuteCopyEntry(TranscriptionHistoryEntry? entry)
    {
        if (entry == null) return;
        System.Windows.Clipboard.SetText(entry.Body);
        ToastService.Instance.Success("Transcript copied to clipboard");
    }

    private void ExecuteDeleteEntry(TranscriptionHistoryEntry? entry)
    {
        if (entry == null) return;

        if (ConfirmationDialog.ShowDestructive("Confirm Delete", "Are you sure you want to delete this entry?",
            confirmText: "Delete", cancelText: "Cancel", centerOnContentArea: true))
        {
            _historyManager.RemoveEntryAsync(entry.Id).ConfigureAwait(false);
            FilteredEntries.Remove(entry);
            _cachedSortedEntries?.RemoveAll(cachedEntry => cachedEntry.Id == entry.Id);
            _totalCountInDb = Math.Max(0, _totalCountInDb - 1);
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(TotalWordCount));
            OnPropertyChanged(nameof(HeaderStats));
            ToastService.Instance.Success("Entry deleted from history");
        }
    }

    private async System.Threading.Tasks.Task ExecuteRerunAsync(TranscriptionHistoryEntry? entry)
    {
        if (entry == null) return;

        if (string.IsNullOrEmpty(entry.AudioFilePath) || !System.IO.File.Exists(entry.AudioFilePath))
        {
            ToastService.Instance.Error("Audio file not found. Cannot rerun transcription.");
            return;
        }

        try
        {
            ToastService.Instance.Info("Rerunning transcription...");

            var session = CreateRerunRecordingSession(entry);

            var app = (App)Application.Current;
            
            // Always use the currently selected provider for reruns
            var provider = LocalAsrProviderAvailability.ResolveActiveProvider(
                app.Services!.CredentialManager,
                app.Services.OfflineEngineManager,
                persistFallbackToCloud: true);

            // Rerun is decoupled from the capsule widget: reuse the app's real
            // callbacks (toast / tray / refresh) but with a no-op widget callback.
            var callbacks = app.RerunWorkflowCallbacks ?? new AppWorkflowUICallbacks(
                (stage) => { }, // updateWidgetWorkflowStage
                (t, m) => { }, // showWorkflowError
                (t, m, w) => true, // showBalloonTip
                (t, m) => false, // promptUserWithSettingsOption
                () => Refresh(), // refreshHistoryView
                () => { } // refreshPerformanceDiagnostics
            );

            var result = await _workflowService.RunWorkflowAsync(
                session,
                provider,
                null, // capturedWindowContext
                null, // lastWindowContext
                false, // forceClipboardCopy
                false, // isTestRun
                callbacks);

            if (result.Success)
            {
                ToastService.Instance.Success("Transcription updated successfully");
                // History will automatically refresh via HistoryChanged event
            }
            else
            {
                ToastService.Instance.Error($"Rerun failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error($"Unexpected error during rerun: {ex.Message}");
        }
    }

    internal static RecordingSession CreateRerunRecordingSession(TranscriptionHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var startTime = DateTime.SpecifyKind(entry.Timestamp, DateTimeKind.Local);
        var audioDuration = ResolveRerunAudioDuration(entry);

        return new RecordingSession
        {
            Id = entry.Id,
            FilePath = entry.AudioFilePath ?? string.Empty,
            StartTime = startTime,
            EndTime = startTime.Add(audioDuration),
            Mode = RecordingMode.Unattended,
            Status = RecordingStatus.Completed
        };
    }

    private static TimeSpan ResolveRerunAudioDuration(TranscriptionHistoryEntry entry)
    {
        if (entry.AudioDuration > TimeSpan.Zero)
        {
            return entry.AudioDuration;
        }

        return TryReadAudioDuration(entry.AudioFilePath);
    }

    private static TimeSpan TryReadAudioDuration(string? audioFilePath)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
        {
            return TimeSpan.Zero;
        }

        try
        {
            using var reader = new WaveFileReader(audioFilePath);
            return reader.TotalTime > TimeSpan.Zero ? reader.TotalTime : TimeSpan.Zero;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            Logger.Warn("HistoryViewModel", $"Could not read audio duration for rerun from '{audioFilePath}': {ex.Message}");
            return TimeSpan.Zero;
        }
    }

    private void ExecuteDownloadAudio(TranscriptionHistoryEntry? entry)
    {
        if (entry == null) return;

        if (string.IsNullOrEmpty(entry.AudioFilePath) || !System.IO.File.Exists(entry.AudioFilePath))
        {
            ToastService.Instance.Error("Audio file not found.");
            return;
        }

        try
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = entry.Id.ToString(),
                DefaultExt = ".wav",
                Filter = "WAV Audio (*.wav)|*.wav"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                System.IO.File.Copy(entry.AudioFilePath, saveFileDialog.FileName, true);
                ToastService.Instance.Success("Audio file saved successfully");
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error($"Failed to download audio: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task ExecuteToggleTranscriptDisplayAsync(TranscriptionHistoryEntry? entry)
    {
        if (entry == null || !entry.CanToggleTranscriptDisplay)
        {
            return;
        }

        string nextMode = entry.IsShowingAsrTranscript
            ? TranscriptionHistoryEntry.TranscriptDisplayModeCleaner
            : TranscriptionHistoryEntry.TranscriptDisplayModeAsr;

        bool updated = await _historyManager.UpdateTranscriptDisplayModeAsync(entry.Id, nextMode);
        if (updated)
        {
            string sourceName = entry.IsShowingAsrTranscript ? "ASR transcript" : "cleaner transcript";
            ToastService.Instance.Success($"Showing {sourceName}");
        }
        else
        {
            ToastService.Instance.Error("Could not update transcript display");
        }
    }

    private void ExecuteStartEdit(TranscriptionHistoryEntry? entry)
    {
        if (entry == null) return;
        if (!entry.CanEditDisplayedTranscript)
        {
            ToastService.Instance.Info("Switch to the cleaner transcript to edit this entry.");
            return;
        }

        EditingId = entry.Id;
        EditingText = entry.TranscribedText;
    }

    private async System.Threading.Tasks.Task ExecuteSaveEditAsync(TranscriptionHistoryEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(EditingText)) return;

        try
        {
            string originalTextForDiff = entry.OriginalTranscribedText ?? entry.TranscribedText;
            string editedText = EditingText;

            // 1. Persist the edit to database
            await _historyManager.UpdateEntryAsync(entry.Id, editedText);
            
            // 2. Refresh the local UI object immediately to update the card preview
            var localEntry = FilteredEntries.FirstOrDefault(e => e.Id == entry.Id);
            if (localEntry != null)
            {
                localEntry.TranscribedText = editedText;
                // Force UI to see property changes on the individual entry
                OnPropertyChanged(nameof(FilteredEntries)); 
            }

            // 3. Clear editing state
            EditingId = null;
            EditingText = string.Empty;
            ToastService.Instance.Success("Transcription updated");

            // 4. Learning Loop: Detect corrections for dictionary
            // Only show popup on FIRST edit - subsequent edits are user fixing their own typos
            if (!entry.HasShownDictionaryPopup)
            {
                // Only show dictionary dialog if word count is unchanged
                // If user added or removed words, it's likely editorial (not ASR correction)
                var originalWordCount = originalTextForDiff.Split(new[] { ' ', '\t', '\n', '\r' },
                    StringSplitOptions.RemoveEmptyEntries).Length;
                var editedWordCount = editedText.Split(new[] { ' ', '\t', '\n', '\r' },
                    StringSplitOptions.RemoveEmptyEntries).Length;

                if (originalWordCount != editedWordCount)
                {
                    // Word count changed - skip dictionary dialog (user added/removed words)
                    entry.HasShownDictionaryPopup = true;
                    await _historyManager.MarkDictionaryPopupShownAsync(entry.Id);
                    return; // Early exit - no dictionary learning for structural changes
                }

                // Same word count - check for word modifications (likely ASR corrections)
                var diffs = _textDiffService.GetMeaningfulDifferences(originalTextForDiff, editedText);
                if (diffs != null && diffs.Any())
                {
                    // Capture selected changes from dialog (shown on UI thread)
                    IReadOnlyList<TextDiff>? selectedChanges = null;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // Show dictionary dialog centered on page content area
                            selectedChanges = Views.AddToDictionaryDialog.Show(diffs, centerOnContentArea: true);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("HistoryViewModel", $"Failed to show dictionary dialog: {ex.Message}", ex);
                            ToastService.Instance.Error("Could not show dictionary learning dialog");
                        }
                    });

                    // Mark popup as shown (regardless of user choice - skip or apply)
                    entry.HasShownDictionaryPopup = true;
                    await _historyManager.MarkDictionaryPopupShownAsync(entry.Id);

                    // Process dictionary additions OUTSIDE Dispatcher.Invoke so we can await properly
                    if (selectedChanges != null && selectedChanges.Any())
                    {
                        int addedCount = 0;
                        foreach (var change in selectedChanges)
                        {
                            try
                            {
                                if (change.Type == DiffType.Added)
                                {
                                    await _dictionaryManager.AddVocabularyWordAsync(change.CorrectedText);
                                    addedCount++;
                                }
                                else if (change.Type == DiffType.Modified)
                                {
                                    await _dictionaryManager.AddCorrectionPairAsync(change.OriginalText, change.CorrectedText);
                                    addedCount++;
                                }
                                // Note: Removed diffs are not processed or counted
                            }
                            catch (ArgumentException) { /* Skip duplicates */ }
                        }

                        if (addedCount > 0)
                        {
                            ToastService.Instance.Success($"Added {addedCount} correction(s) to dictionary");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error($"Failed to save edit: {ex.Message}");
        }
    }

    private void ExecuteCancelEdit()
    {
        EditingId = null;
        EditingText = string.Empty;
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                Refresh();
            }
        }
    }

    public ObservableCollection<TranscriptionHistoryEntry> FilteredEntries
    {
        get => _filteredEntries;
        set => SetProperty(ref _filteredEntries, value);
    }

    public int TotalCount => _totalCountInDb;

    public int TotalWordCount => _cachedSortedEntries?.Sum(entry => entry.WordCount) ?? 0;

    public string HeaderStats => $"{TotalCount:N0} transcripts - {TotalWordCount:N0} words";

    public int LoadedCount
    {
        get => _loadedCount;
        set => SetProperty(ref _loadedCount, value);
    }

    public int RenderedCount
    {
        get => _renderedCount;
        set => SetProperty(ref _renderedCount, value);
    }

    public bool HasMoreItems => _loadedCount < _totalCountInDb;

    public void LoadMore()
    {
        if (_isLoading || !HasMoreItems) return;

        _isLoading = true;
        try
        {
            if (_cachedSortedEntries == null) return;
            
            var batch = _cachedSortedEntries
                .Skip(_loadedCount)
                .Take(BatchSize)
                .ToList();
            
            foreach (var entry in batch)
            {
                var dateKey = entry.Timestamp.ToString("yyyy-MM-dd");
                bool isNewDay = dateKey != _lastDateKey;
                
                entry.IsFirstOfDay = isNewDay && FilteredEntries.Count > 0;
                entry.DateLabel = entry.Timestamp.Date == DateTime.Today ? "TODAY" : 
                                 entry.Timestamp.Date == DateTime.Today.AddDays(-1) ? "YESTERDAY" : 
                                 entry.Timestamp.ToString("MMMM d, yyyy").ToUpper();
                _lastDateKey = dateKey;

                FilteredEntries.Add(entry);
            }

            LoadedCount = FilteredEntries.Count;
            RenderedCount = FilteredEntries.Count;
            OnPropertyChanged(nameof(HasMoreItems));
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void Refresh()
    {
        FilteredEntries.Clear();
        _loadedCount = 0;
        _lastDateKey = null;
        
        _cachedSortedEntries = _historyManager.GetAllEntries()
            .Where(e => string.IsNullOrEmpty(SearchQuery) || 
                       e.Body.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Timestamp)
            .ToList();
        
        _totalCountInDb = _cachedSortedEntries.Count;
        
        LoadMore();
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TotalWordCount));
        OnPropertyChanged(nameof(HeaderStats));
        OnPropertyChanged(nameof(HasMoreItems));
    }

    public void RefreshNow()
    {
        if (!_disposed)
        {
            Refresh();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _historyManager.HistoryChanged -= OnHistoryChanged;
    }
}
