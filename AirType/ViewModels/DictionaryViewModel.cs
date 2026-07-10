using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using AirType.Models.Dictionary;
using AirType.Services;
using AirType.Services.Dictionary;
using AirType.Views;

namespace AirType.ViewModels;

public class DictionaryViewModel : BaseViewModel
{
    private readonly IDictionaryManager _dictionaryManager;
    private readonly List<DictionaryEntry> _allEntries = new();
    private string _searchQuery = string.Empty;
    private bool _isAddEntryPanelVisible = false;
    private string _selectedTab = "vocabulary";
    private string _wordInput = string.Empty;
    private string _fromInput = string.Empty;
    private string _toInput = string.Empty;
    private DictionaryEntry? _editingEntry;

    public ObservableCollection<DictionaryEntry> FilteredEntries { get; } = new();

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                RefreshFilteredEntries();
            }
        }
    }

    public bool IsAddEntryPanelVisible
    {
        get => _isAddEntryPanelVisible;
        set => SetProperty(ref _isAddEntryPanelVisible, value);
    }

    public string SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(IsVocabularyTabSelected));
                OnPropertyChanged(nameof(IsCorrectionTabSelected));
                RefreshFilteredEntries();
            }
        }
    }

    public bool IsVocabularyTabSelected => SelectedTab == "vocabulary";
    public bool IsCorrectionTabSelected => SelectedTab == "correction";
    public bool IsEditingEntry => _editingEntry != null;
    public bool IsEntryTypeSelectorEnabled => !IsEditingEntry;
    public string EntryPanelTitle => IsEditingEntry ? "Edit Entry" : "Add Entry";
    public string SaveWordButtonText => IsEditingEntry ? "Save Changes" : "Save Word";
    public string SaveCorrectionButtonText => IsEditingEntry ? "Save Changes" : "Save Correction";

    public string WordInput
    {
        get => _wordInput;
        set
        {
            if (SetProperty(ref _wordInput, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string FromInput
    {
        get => _fromInput;
        set
        {
            if (SetProperty(ref _fromInput, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string ToInput
    {
        get => _toInput;
        set
        {
            if (SetProperty(ref _toInput, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StatsText
    {
        get
        {
            var totalCount = FilteredEntries.Count;
            var wordCount = FilteredEntries.Count(e => e.EntryType == DictionaryEntryType.VocabularyWord);
            var correctionCount = FilteredEntries.Count(e => e.EntryType == DictionaryEntryType.CorrectionPair);
            return $"{totalCount} entries total ({wordCount} words, {correctionCount} corrections)";
        }
    }

    public ICommand AddEntryCommand { get; }
    public ICommand SaveWordCommand { get; }
    public ICommand SaveCorrectionCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand EditEntryCommand { get; }
    public ICommand DeleteEntryCommand { get; }

    public DictionaryViewModel(IDictionaryManager dictionaryManager)
    {
        _dictionaryManager = dictionaryManager ?? throw new ArgumentNullException(nameof(dictionaryManager));
        
        AddEntryCommand = new RelayCommand(ToggleAddEntryPanel);
        SaveWordCommand = new RelayCommand(SaveWord, () => !string.IsNullOrWhiteSpace(WordInput));
        SaveCorrectionCommand = new RelayCommand(SaveCorrection, () => !string.IsNullOrWhiteSpace(FromInput) && !string.IsNullOrWhiteSpace(ToInput));
        CancelCommand = new RelayCommand(Cancel);
        EditEntryCommand = new RelayCommand<DictionaryEntry>(EditEntry);
        DeleteEntryCommand = new RelayCommand<DictionaryEntry>(DeleteEntry);

        LoadFromManager();
    }

    private void LoadFromManager()
    {
        _allEntries.Clear();
        _allEntries.AddRange(_dictionaryManager.GetAllEntries());
        RefreshFilteredEntries();
    }

    private void RefreshFilteredEntries()
    {
        var selectedType = SelectedTab == "vocabulary"
            ? DictionaryEntryType.VocabularyWord
            : DictionaryEntryType.CorrectionPair;

        var filtered = _allEntries.Where(e => e.EntryType == selectedType);

        filtered = string.IsNullOrWhiteSpace(SearchQuery)
            ? filtered
            : filtered.Where(e =>
                (e.Word?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.OriginalText?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.CorrectedText?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ?? false));

        filtered = filtered
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.ModifiedAt);

        FilteredEntries.Clear();
        foreach (var entry in filtered)
        {
            FilteredEntries.Add(entry);
        }

        OnPropertyChanged(nameof(StatsText));
    }

    private void ToggleAddEntryPanel()
    {
        if (IsAddEntryPanelVisible && !IsEditingEntry)
        {
            Cancel();
            return;
        }

        ClearEditingEntry();
        ClearEntryInputs();
        IsAddEntryPanelVisible = true;
    }

    private async void SaveWord()
    {
        try
        {
            if (IsEditingEntry)
            {
                var updated = await UpdateVocabularyWordAsync();
                ReplaceLocalEntry(updated);
                ToastService.Instance.Success("Dictionary entry updated");
            }
            else
            {
                var entry = await _dictionaryManager.AddVocabularyWordAsync(WordInput.Trim());
                _allEntries.Add(entry);
                ToastService.Instance.Success("Word added to dictionary");
            }

            RefreshFilteredEntries();
            ResetEntryForm();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error($"Failed to save dictionary entry: {ex.Message}");
        }
    }

    private async void SaveCorrection()
    {
        try
        {
            if (IsEditingEntry)
            {
                var updated = await UpdateCorrectionPairAsync();
                ReplaceLocalEntry(updated);
                ToastService.Instance.Success("Dictionary entry updated");
            }
            else
            {
                var entry = await _dictionaryManager.AddCorrectionPairAsync(FromInput.Trim(), ToInput.Trim());
                _allEntries.Add(entry);
                ToastService.Instance.Success("Correction added to dictionary");
            }

            RefreshFilteredEntries();
            ResetEntryForm();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error($"Failed to save dictionary entry: {ex.Message}");
        }
    }

    private void EditEntry(DictionaryEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        _editingEntry = entry;

        if (entry.EntryType == DictionaryEntryType.VocabularyWord)
        {
            SelectedTab = "vocabulary";
            WordInput = entry.Word ?? string.Empty;
            FromInput = string.Empty;
            ToInput = string.Empty;
        }
        else
        {
            SelectedTab = "correction";
            WordInput = string.Empty;
            FromInput = entry.OriginalText ?? string.Empty;
            ToInput = entry.CorrectedText ?? string.Empty;
        }

        IsAddEntryPanelVisible = true;
        NotifyEditingStateChanged();
    }

    private async Task<DictionaryEntry> UpdateVocabularyWordAsync()
    {
        var existing = _editingEntry ?? throw new InvalidOperationException("No dictionary entry is being edited.");
        var updated = new DictionaryEntry
        {
            Id = existing.Id,
            CreatedAt = existing.CreatedAt,
            ModifiedAt = existing.ModifiedAt,
            EntryType = DictionaryEntryType.VocabularyWord,
            Word = WordInput.Trim(),
            OriginalText = null,
            CorrectedText = null
        };

        return await _dictionaryManager.UpdateEntryAsync(updated);
    }

    private async Task<DictionaryEntry> UpdateCorrectionPairAsync()
    {
        var existing = _editingEntry ?? throw new InvalidOperationException("No dictionary entry is being edited.");
        var updated = new DictionaryEntry
        {
            Id = existing.Id,
            CreatedAt = existing.CreatedAt,
            ModifiedAt = existing.ModifiedAt,
            EntryType = DictionaryEntryType.CorrectionPair,
            Word = null,
            OriginalText = FromInput.Trim(),
            CorrectedText = ToInput.Trim()
        };

        return await _dictionaryManager.UpdateEntryAsync(updated);
    }

    private void ReplaceLocalEntry(DictionaryEntry updated)
    {
        int index = _allEntries.FindIndex(entry => entry.Id == updated.Id);
        if (index >= 0)
        {
            _allEntries[index] = updated;
        }
        else
        {
            _allEntries.Add(updated);
        }
    }

    private void Cancel()
    {
        ResetEntryForm();
    }

    private void ResetEntryForm()
    {
        ClearEditingEntry();
        ClearEntryInputs();
        IsAddEntryPanelVisible = false;
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearEntryInputs()
    {
        WordInput = string.Empty;
        FromInput = string.Empty;
        ToInput = string.Empty;
    }

    private void ClearEditingEntry()
    {
        if (_editingEntry == null)
        {
            NotifyEditingStateChanged();
            return;
        }

        _editingEntry = null;
        NotifyEditingStateChanged();
    }

    private void NotifyEditingStateChanged()
    {
        OnPropertyChanged(nameof(IsEditingEntry));
        OnPropertyChanged(nameof(IsEntryTypeSelectorEnabled));
        OnPropertyChanged(nameof(EntryPanelTitle));
        OnPropertyChanged(nameof(SaveWordButtonText));
        OnPropertyChanged(nameof(SaveCorrectionButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    private async void DeleteEntry(DictionaryEntry? entry)
    {
        if (entry == null) return;

        if (ConfirmationDialog.ShowDestructive("Confirm Delete", "Are you sure you want to delete this entry?",
            confirmText: "Delete", cancelText: "Cancel", centerOnContentArea: true))
        {
            bool success = await _dictionaryManager.DeleteEntryAsync(entry.Id);
            if (success)
            {
                _allEntries.Remove(entry);
                RefreshFilteredEntries();
                ToastService.Instance.Success("Entry deleted");
            }
            else
            {
                ToastService.Instance.Error("Failed to delete entry from dictionary");
            }
        }
    }
}
