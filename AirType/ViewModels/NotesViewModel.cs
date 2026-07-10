using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AirType.Models;
using AirType.Services;
using AirType.Services.Database;

namespace AirType.ViewModels;

public class NotesViewModel : BaseViewModel, IDisposable
{
    private readonly NotesDatabase _database;
    private readonly WidgetStateManager? _stateManager;

    private string _newNoteContent = string.Empty;
    private string _searchQuery = string.Empty;
    private bool _isListView = false;
    private bool _isRecording;
    private bool _isTranscribing;
    private bool _isNotesInitiated;
    private bool _disposed;

    public NotesViewModel(NotesDatabase database, WidgetStateManager? stateManager = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _stateManager = stateManager;

        Notes = new ObservableCollection<Note>();

        AddNoteCommand = new RelayCommand(AddNote, CanAddNote);
        EditNoteCommand = new RelayCommand<Note>(EditNote);
        DeleteNoteCommand = new RelayCommand<Note>(DeleteNote);
        ToggleViewCommand = new RelayCommand(ToggleView);

        // Subscribe to capsule state changes for mic button visual sync
        if (_stateManager != null)
        {
            _stateManager.StateChanged += OnCapsuleStateChanged;

            // Initialize state from current capsule state
            SyncStateFromCapsule(_stateManager.CurrentState);
        }

        LoadNotes();
    }

    #region Properties

    public ObservableCollection<Note> Notes { get; }

    public string NewNoteContent
    {
        get => _newNoteContent;
        set => SetProperty(ref _newNoteContent, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                SearchNotes();
            }
        }
    }

    public bool IsListView
    {
        get => _isListView;
        set => SetProperty(ref _isListView, value);
    }

    /// <summary>
    /// Whether recording is currently active (synced with capsule state)
    /// </summary>
    public bool IsRecording
    {
        get => _isRecording;
        private set => SetProperty(ref _isRecording, value);
    }

    /// <summary>
    /// Whether transcription is in progress (synced with capsule state)
    /// </summary>
    public bool IsTranscribing
    {
        get => _isTranscribing;
        private set => SetProperty(ref _isTranscribing, value);
    }

    #endregion

    #region Commands

    public ICommand AddNoteCommand { get; }
    public ICommand EditNoteCommand { get; }
    public ICommand DeleteNoteCommand { get; }
    public ICommand ToggleViewCommand { get; }

    #endregion

    #region Capsule State Sync

    /// <summary>
    /// Marks that the current/upcoming recording was initiated from the Notes page mic button.
    /// Call this before triggering capsule recording to enable visual state sync.
    /// </summary>
    public void SetNotesInitiated()
    {
        _isNotesInitiated = true;
    }

    private void OnCapsuleStateChanged(object? sender, WidgetStateChangedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Only update visual state if recording was initiated from Notes page
            if (_isNotesInitiated)
            {
                SyncStateFromCapsule(e.NewState);
            }

            // Reset flag when recording cycle completes (returns to idle)
            if (e.NewState == WidgetStateManager.WidgetState.IdleMinimal)
            {
                _isNotesInitiated = false;
            }
        });
    }

    private void SyncStateFromCapsule(WidgetStateManager.WidgetState state)
    {
        IsRecording = state == WidgetStateManager.WidgetState.Recording;
        IsTranscribing = state is WidgetStateManager.WidgetState.Transcribing or WidgetStateManager.WidgetState.Cleaning;
    }

    #endregion

    #region Note CRUD Operations

    private void LoadNotes()
    {
        Notes.Clear();
        var notes = string.IsNullOrWhiteSpace(_searchQuery) ?
            _database.GetAllNotes() :
            _database.SearchNotes(_searchQuery);

        foreach (var note in notes)
        {
            Notes.Add(note);
        }
    }

    private void SearchNotes()
    {
        LoadNotes();
    }

    private bool CanAddNote() => !string.IsNullOrWhiteSpace(_newNoteContent);

    private void AddNote()
    {
        if (!CanAddNote()) return;

        var note = new Note
        {
            Date = DateTime.Now,
            Content = _newNoteContent.Trim()
        };

        _database.AddNote(note);
        Notes.Insert(0, note); // Add to top since ordered by date DESC
        NewNoteContent = string.Empty;
    }

    private void EditNote(Note note)
    {
        // Show edit dialog using the global ModalWindow (centered on page content area)
        var editContent = new Views.EditNoteContent(note);
        var result = Views.ModalWindow.Show<Views.EditNoteResult>(editContent, centerOnContentArea: true);

        if (result == null) return;

        switch (result.Action)
        {
            case Views.EditNoteAction.Save:
                SaveNoteChanges(note, result.Content!);
                break;

            case Views.EditNoteAction.Delete:
                PerformDeleteNote(note);
                break;

            case Views.EditNoteAction.Cancel:
            default:
                // Nothing to do
                break;
        }
    }

    private void SaveNoteChanges(Note note, string newContent)
    {
        note.Content = newContent;
        note.EditedAt = DateTime.Now;

        _database.UpdateNote(note);

        // Force UI refresh
        var index = Notes.IndexOf(note);
        if (index >= 0)
        {
            Notes[index] = note;
            OnPropertyChanged(nameof(Notes));
        }

        ToastService.Instance.Success("Note updated");
    }

    private void DeleteNote(Note note)
    {
        // Show confirmation dialog (centered on page content area)
        bool confirmed = Views.ConfirmationDialog.ShowDestructive(
            "Delete Note?",
            "This action cannot be undone. The note will be permanently removed.",
            "Delete",
            "Cancel",
            centerOnContentArea: true);

        if (confirmed)
        {
            PerformDeleteNote(note);
        }
    }

    private void PerformDeleteNote(Note note)
    {
        _database.DeleteNote(note.Id);
        Notes.Remove(note);
        ToastService.Instance.Success("Note deleted");
    }

    private void ToggleView()
    {
        IsListView = !IsListView;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            if (_stateManager != null)
            {
                _stateManager.StateChanged -= OnCapsuleStateChanged;
            }
        }

        _disposed = true;
    }

    #endregion
}
