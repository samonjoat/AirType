using System.ComponentModel;

namespace AirType.Models;

/// <summary>
/// Represents a single text change (difference) between original and edited text.
/// Used to track word-level changes for the add-to-dictionary flow.
/// </summary>
public class TextDiff : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _originalText = string.Empty;
    private string _correctedText = string.Empty;
    private DiffType _type;
    private bool _isEditing;
    private string _editingText = string.Empty;
    private string _editingOriginalText = string.Empty;

    /// <summary>
    /// The original text segment (what the transcription API produced).
    /// </summary>
    public string OriginalText
    {
        get => _originalText;
        set { _originalText = value; OnPropertyChanged(nameof(OriginalText)); }
    }

    /// <summary>
    /// The corrected/edited text segment (what the user changed it to).
    /// </summary>
    public string CorrectedText
    {
        get => _correctedText;
        set { _correctedText = value; OnPropertyChanged(nameof(CorrectedText)); }
    }

    /// <summary>
    /// The type of change: Added, Removed, or Modified.
    /// </summary>
    public DiffType Type
    {
        get => _type;
        set
        {
            if (_type == value)
            {
                return;
            }

            _type = value;
            OnPropertyChanged(nameof(Type));
            OnPropertyChanged(nameof(IsAdded));
            OnPropertyChanged(nameof(IsModified));
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(IsMeaningfulChange));
        }
    }

    /// <summary>
    /// Whether this item is currently being edited in the UI.
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); }
    }

    /// <summary>
    /// Temporary text being edited for corrected text (before save).
    /// </summary>
    public string EditingText
    {
        get => _editingText;
        set { _editingText = value; OnPropertyChanged(nameof(EditingText)); }
    }

    /// <summary>
    /// Temporary text being edited for original text (before save).
    /// Enables multi-word corrections like "Solomon Joot" → "Samon the Joat".
    /// </summary>
    public string EditingOriginalText
    {
        get => _editingOriginalText;
        set { _editingOriginalText = value; OnPropertyChanged(nameof(EditingOriginalText)); }
    }

    /// <summary>
    /// Whether this diff is an Added type (for conditional UI display).
    /// </summary>
    public bool IsAdded => Type == DiffType.Added;

    /// <summary>
    /// Whether this diff is a Modified type (for conditional UI display).
    /// </summary>
    public bool IsModified => Type == DiffType.Modified;

    /// <summary>
    /// Whether this change is selected for adding to the dictionary.
    /// (Kept for backward compatibility - now all visible items are added)
    /// </summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// Confidence score for this match (0.0 - 1.0).
    /// Higher values indicate more likely ASR correction vs editorial change.
    /// - 0.95: Phonetic match (sounds alike)
    /// - 0.85: Edit distance &lt; 30% of word length
    /// - 0.70: Edit distance 30-50%
    /// - 0.50: Edit distance 50-80%
    /// - 0.60: Pure addition (no original to match)
    /// - 0.00: Pure deletion or grammar-only change
    /// </summary>
    public double Confidence { get; set; } = 0.0;

    /// <summary>
    /// Whether this diff meets the confidence threshold (80%) for suggestion.
    /// </summary>
    public bool MeetsConfidenceThreshold => Confidence >= 0.80;

    /// <summary>
    /// Display text for UI (shows the change in "original → corrected" format).
    /// </summary>
    public string DisplayText => Type switch
    {
        DiffType.Added => $"[Added] {CorrectedText}",
        DiffType.Removed => $"[Removed] {OriginalText}",
        DiffType.Modified => $"{OriginalText} → {CorrectedText}",
        _ => $"{OriginalText} → {CorrectedText}"
    };
    
    /// <summary>
    /// Indicates if this change is a meaningful correction (not just whitespace/punctuation).
    /// </summary>
    public bool IsMeaningfulChange
    {
        get
        {
            // Skip if both are empty or just whitespace
            if (string.IsNullOrWhiteSpace(OriginalText) && string.IsNullOrWhiteSpace(CorrectedText))
                return false;
            
            // If modified, check for actual word change (ignore case/punct)
            if (Type == DiffType.Modified)
            {
                if (OriginalText.Equals(CorrectedText, System.StringComparison.OrdinalIgnoreCase))
                    return false;
                
                var originalClean = System.Text.RegularExpressions.Regex.Replace(OriginalText, @"[^\w\s]", "").Trim();
                var correctedClean = System.Text.RegularExpressions.Regex.Replace(CorrectedText, @"[^\w\s]", "").Trim();
                return !originalClean.Equals(correctedClean, System.StringComparison.OrdinalIgnoreCase);
            }

            // Always consider Added/Removed words as meaningful for dictionary learning
            return Type == DiffType.Added || Type == DiffType.Removed;
        }
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Type of text difference.
/// </summary>
public enum DiffType
{
    /// <summary>
    /// Text was added (not in original).
    /// </summary>
    Added,
    
    /// <summary>
    /// Text was removed (not in edited).
    /// </summary>
    Removed,
    
    /// <summary>
    /// Text was modified (different in original vs edited).
    /// </summary>
    Modified
}
