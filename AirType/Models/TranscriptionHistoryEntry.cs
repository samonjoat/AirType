using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AirType.Models;

/// <summary>
/// Represents a single transcription in the history
/// </summary>
public class TranscriptionHistoryEntry : INotifyPropertyChanged
{
    public const string TranscriptDisplayModeCleaner = "Cleaner";
    public const string TranscriptDisplayModeAsr = "Asr";

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _transcribedText = string.Empty;
    private int _wordCount;
    private string _provider = "Unknown";
    private string _modelUsed = "Unknown";
    private string? _rawTranscribedText;
    private string? _cleanupModel;
    private string? _cleanupStatus;
    private string _transcriptDisplayMode = TranscriptDisplayModeCleaner;

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string TranscribedText
    {
        get => _transcribedText;
        set
        {
            if (_transcribedText != value)
            {
                _transcribedText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FinalTranscribedText));
                NotifyDisplayedTranscriptProperties();
            }
        }
    }

    public int WordCount
    {
        get => _wordCount;
        set
        {
            if (_wordCount != value)
            {
                _wordCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Words));
            }
        }
    }
    public TimeSpan AudioDuration { get; set; }
    public string Provider
    {
        get => _provider;
        set
        {
            if (_provider != value)
            {
                _provider = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProviderDisplay));
                OnPropertyChanged(nameof(SpeechEngineDisplay));
                OnPropertyChanged(nameof(AsrModelDisplay));
                OnPropertyChanged(nameof(TranscriptSourceModelDisplay));
            }
        }
    }

    public string ModelUsed
    {
        get => _modelUsed;
        set
        {
            if (_modelUsed != value)
            {
                _modelUsed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Model));
                OnPropertyChanged(nameof(ProviderDisplay));
                OnPropertyChanged(nameof(SpeechEngineDisplay));
                OnPropertyChanged(nameof(CleanerDisplay));
                OnPropertyChanged(nameof(AsrModelDisplay));
                OnPropertyChanged(nameof(TranscriptSourceModelDisplay));
            }
        }
    }
    
    // UI-compatible properties for HistoryView.xaml
    public string Time => Timestamp.ToString("t");
    public string Body => IsShowingAsrTranscript && !string.IsNullOrWhiteSpace(RawTranscribedText)
        ? RawTranscribedText!
        : TranscribedText;
    public string FinalTranscribedText
    {
        get => TranscribedText;
        set => TranscribedText = value;
    }
    public int Words => CountWords(Body);
    public double LatencySeconds { get; set; }
    public string Duration
    {
        get
        {
            int totalSeconds = Math.Max(0, (int)AudioDuration.TotalSeconds);
            if (totalSeconds < 1) return "0s";

            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;

            if (hours > 0)
            {
                return minutes > 0 || seconds > 0
                    ? $"{hours}h{minutes}m{seconds}s"
                    : $"{hours}h";
            }

            if (minutes > 0)
            {
                return $"{minutes}m{seconds}s";
            }

            return $"{seconds}s";
        }
    }
    public string SpeechEngineDisplay => FormatSpeechEngineDisplay(Provider, ModelUsed, ProviderChain, FallbackReason, RawProvider, ActualProvider);
    public string CleanerDisplay => FormatCleanerDisplay(ModelUsed, CleanupModel, CleanupStatus);
    public string AsrModelDisplay => FormatAsrModelDisplay();
    public string TranscriptSourceDisplay => IsShowingAsrTranscript ? "ASR" : "Cleaner";
    public string TranscriptSourceModelDisplay => IsShowingAsrTranscript ? AsrModelDisplay : CleanerDisplay;
    public bool HasCleanerTranscript =>
        IsCleanupSucceeded(CleanupStatus) &&
        !string.IsNullOrWhiteSpace(TranscribedText) &&
        !string.IsNullOrWhiteSpace(RawTranscribedText);
    public bool IsShowingAsrTranscript => EffectiveTranscriptDisplayMode == TranscriptDisplayModeAsr;
    public bool CanToggleTranscriptDisplay => HasCleanerTranscript;
    public bool CanEditDisplayedTranscript => !HasCleanerTranscript || !IsShowingAsrTranscript;
    public string TranscriptDisplayToggleHeader => IsShowingAsrTranscript ? "Show Cleaner Transcript" : "Show ASR Transcript";
    public string TranscriptDisplayToggleAutomationName => IsShowingAsrTranscript ? "Show cleaner transcript" : "Show ASR transcript";
    public string EffectiveTranscriptDisplayMode =>
        HasCleanerTranscript && string.Equals(TranscriptDisplayMode, TranscriptDisplayModeCleaner, StringComparison.OrdinalIgnoreCase)
            ? TranscriptDisplayModeCleaner
            : TranscriptDisplayModeAsr;
    public string ProviderDisplay => SpeechEngineDisplay;
    public string Model => CleanerDisplay;
    public string DateLabel { get; set; } = string.Empty;
    public bool IsFirstOfDay { get; set; }
    public bool InjectionSucceeded { get; set; }
    public string? InjectionErrorMessage { get; set; }
    public string? TargetWindowTitle { get; set; }
    public string? AudioFilePath { get; set; }

    public string? RawTranscribedText
    {
        get => _rawTranscribedText;
        set
        {
            if (_rawTranscribedText != value)
            {
                _rawTranscribedText = value;
                OnPropertyChanged();
                NotifyDisplayedTranscriptProperties();
                OnPropertyChanged(nameof(AsrModelDisplay));
                OnPropertyChanged(nameof(TranscriptSourceModelDisplay));
            }
        }
    }

    public string? ProviderChain { get; set; }
    public string? RawProvider { get; set; }
    public string? RawModel { get; set; }
    public string? CleanupProvider { get; set; }
    public string? CleanupModel
    {
        get => _cleanupModel;
        set
        {
            if (_cleanupModel != value)
            {
                _cleanupModel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CleanerDisplay));
                OnPropertyChanged(nameof(TranscriptSourceModelDisplay));
            }
        }
    }

    public string? CleanupStatus
    {
        get => _cleanupStatus;
        set
        {
            if (_cleanupStatus != value)
            {
                _cleanupStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CleanerDisplay));
                NotifyDisplayedTranscriptProperties();
            }
        }
    }

    public string TranscriptDisplayMode
    {
        get => _transcriptDisplayMode;
        set
        {
            string normalized = NormalizeTranscriptDisplayMode(value);
            if (_transcriptDisplayMode != normalized)
            {
                _transcriptDisplayMode = normalized;
                OnPropertyChanged();
                NotifyDisplayedTranscriptProperties();
            }
        }
    }

    public double? AsrLatencySeconds { get; set; }
    public double? CleanupLatencySeconds { get; set; }
    public double? TotalLatencySeconds { get; set; }
    public int? LocalFlushWaitMs { get; set; }
    public string? FallbackReason { get; set; }
    public string? WorkerSessionId { get; set; }
    public int? WorkerUtteranceCount { get; set; }
    public int? HotwordTokenCount { get; set; }
    public int? InitialPromptMaxTokenCount { get; set; }
    public string? RequestedProvider { get; set; }
    public string? RequestedModel { get; set; }
    public string? ActualProvider { get; set; }
    public string? ActualModel { get; set; }
    public string? WorkflowStatus { get; set; }
    public string? WorkflowError { get; set; }
    
    /// <summary>
    /// Stores the original transcribed text before any edits.
    /// Null if the entry has never been edited.
    /// </summary>
    public string? OriginalTranscribedText { get; set; }
    
    /// <summary>
    /// Timestamp of when the entry was last edited.
    /// Null if the entry has never been edited.
    /// </summary>
    public DateTime? EditedAt { get; set; }
    
    /// <summary>
    /// Indicates whether this entry has been edited by the user.
    /// </summary>
    public bool HasBeenEdited => OriginalTranscribedText != null;

    /// <summary>
    /// Tracks if the dictionary learning popup has been shown for this entry.
    /// Once shown (on first edit), subsequent edits won't trigger it again.
    /// </summary>
    public bool HasShownDictionaryPopup { get; set; }

    /// <summary>
    /// Tracks the type of session that created this transcription.
    /// Valid values: "Hotkey", "Unattended", "Notes"
    /// </summary>
    public string SessionType { get; set; } = "Hotkey";

    /// <summary>
    /// Gets a preview of the transcribed text (first 100 characters)
    /// </summary>
    public string Preview => Body.Length > 100
        ? Body.Substring(0, 100) + "..."
        : Body;
    
    /// <summary>
    /// Gets a human-readable timestamp (e.g., "2 minutes ago", "Today at 3:45 PM")
    /// </summary>
    public string RelativeTimestamp
    {
        get
        {
            var now = DateTime.Now;
            var diff = now - Timestamp;
            
            if (diff.TotalMinutes < 1)
                return "Just now";
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes} minute{((int)diff.TotalMinutes == 1 ? "" : "s")} ago";
            if (diff.TotalHours < 24 && now.Date == Timestamp.Date)
                return $"Today at {Timestamp:h:mm tt}";
            if (diff.TotalDays < 2 && now.Date.AddDays(-1) == Timestamp.Date)
                return $"Yesterday at {Timestamp:h:mm tt}";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays} days ago";
            
            return Timestamp.ToString("MMM d, yyyy 'at' h:mm tt");
        }
    }
    
    /// <summary>
    /// Gets status icon for display
    /// </summary>
    public string StatusIcon => InjectionSucceeded ? "📋" : "⚠️";
    
    /// <summary>
    /// Gets status message for display
    /// </summary>
    public string StatusMessage
    {
        get
        {
            if (InjectionSucceeded)
                return "Injected successfully";
            if (!string.IsNullOrEmpty(InjectionErrorMessage))
                return $"Injection failed: {InjectionErrorMessage}";
            return "Injection failed - copied to clipboard";
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void NotifyDisplayedTranscriptProperties()
    {
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Words));
        OnPropertyChanged(nameof(HasCleanerTranscript));
        OnPropertyChanged(nameof(IsShowingAsrTranscript));
        OnPropertyChanged(nameof(CanToggleTranscriptDisplay));
        OnPropertyChanged(nameof(CanEditDisplayedTranscript));
        OnPropertyChanged(nameof(TranscriptDisplayToggleHeader));
        OnPropertyChanged(nameof(TranscriptDisplayToggleAutomationName));
        OnPropertyChanged(nameof(TranscriptSourceDisplay));
        OnPropertyChanged(nameof(TranscriptSourceModelDisplay));
        OnPropertyChanged(nameof(EffectiveTranscriptDisplayMode));
    }

    private static string NormalizeTranscriptDisplayMode(string? mode) =>
        string.Equals(mode?.Trim(), TranscriptDisplayModeAsr, StringComparison.OrdinalIgnoreCase)
            ? TranscriptDisplayModeAsr
            : TranscriptDisplayModeCleaner;

    private static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string FormatSpeechEngineDisplay(
        string? provider,
        string? modelUsed,
        string? providerChain,
        string? fallbackReason,
        string? rawProvider,
        string? actualProvider)
    {
        string? structuredProvider = FirstNonEmpty(actualProvider, rawProvider);
        if (!string.IsNullOrWhiteSpace(structuredProvider))
        {
            return IsLocalProvider(structuredProvider) ? "Local" : "Cloud";
        }

        string providerValue = string.IsNullOrWhiteSpace(provider) ? "Unknown" : provider.Trim();
        if (IsLocalProvider(providerValue))
        {
            return HasUnavailableFallback(modelUsed) ||
                   HasUnavailableFallback(providerChain) ||
                   !string.IsNullOrWhiteSpace(fallbackReason)
                ? "Cloud"
                : "Local";
        }

        if (string.Equals(providerValue, "Groq", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerValue, "Gemini", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerValue, "OpenRouter", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerValue, "Cloud", StringComparison.OrdinalIgnoreCase))
        {
            return "Cloud";
        }

        string? firstAvailableModel = SplitModelChain(modelUsed)
            .Where(part => !IsUnavailablePart(part))
            .FirstOrDefault();

        if (firstAvailableModel != null)
        {
            return IsLocalSpeechModel(firstAvailableModel) ? "Local" : "Cloud";
        }

        return providerValue;
    }

    private static bool IsLocalProvider(string provider) =>
        string.Equals(provider.Trim(), "Local", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string FormatCleanerDisplay(string? modelUsed, string? cleanupModel, string? cleanupStatus)
    {
        if (IsCleanupDisabledOrFailed(cleanupStatus))
        {
            return "Off";
        }

        if (!string.IsNullOrWhiteSpace(cleanupModel) &&
            (string.IsNullOrWhiteSpace(cleanupStatus) || IsCleanupSucceeded(cleanupStatus)))
        {
            return FormatModelPart(cleanupModel);
        }

        if (string.IsNullOrWhiteSpace(modelUsed))
        {
            return "Off";
        }

        string[] availableParts = SplitModelChain(modelUsed)
            .Where(part => !IsUnavailablePart(part))
            .ToArray();

        if (availableParts.Length <= 1)
        {
            return "Off";
        }

        string? cleanerModel = availableParts
            .Skip(1)
            .LastOrDefault(part => !IsSpeechModel(part));

        return cleanerModel == null ? "Off" : FormatModelPart(cleanerModel);
    }

    private string FormatAsrModelDisplay()
    {
        string? structuredModel = FirstNonEmpty(ActualModel, RawModel);
        if (!string.IsNullOrWhiteSpace(structuredModel))
        {
            return FormatModelPart(structuredModel);
        }

        string? firstSpeechModel = SplitModelChain(ModelUsed)
            .Where(part => !IsUnavailablePart(part))
            .FirstOrDefault(IsSpeechModel);

        if (!string.IsNullOrWhiteSpace(firstSpeechModel))
        {
            return FormatModelPart(firstSpeechModel);
        }

        return string.IsNullOrWhiteSpace(ModelUsed) ? "ASR" : FormatModelPart(ModelUsed);
    }

    private static bool IsCleanupSucceeded(string? cleanupStatus) =>
        string.Equals(cleanupStatus?.Trim(), "Succeeded", StringComparison.OrdinalIgnoreCase);

    private static bool IsCleanupDisabledOrFailed(string? cleanupStatus) =>
        !string.IsNullOrWhiteSpace(cleanupStatus) &&
        (string.Equals(cleanupStatus.Trim(), "Off", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(cleanupStatus.Trim(), "Disabled", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(cleanupStatus.Trim(), "Skipped", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(cleanupStatus.Trim(), "Failed", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(cleanupStatus.Trim(), "TimedOut", StringComparison.OrdinalIgnoreCase));

    private static bool HasUnavailableFallback(string? modelUsed) =>
        !string.IsNullOrWhiteSpace(modelUsed) &&
        modelUsed.Contains(" unavailable -> ", StringComparison.OrdinalIgnoreCase);

    private static string[] SplitModelChain(string? modelUsed) =>
        string.IsNullOrWhiteSpace(modelUsed)
            ? Array.Empty<string>()
            : modelUsed
                .Split(new[] { "->" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();

    private static bool IsUnavailablePart(string modelPart) =>
        modelPart.Contains(" unavailable", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalSpeechModel(string modelPart) =>
        modelPart.StartsWith("faster-whisper-", StringComparison.OrdinalIgnoreCase) ||
        modelPart.StartsWith("whisper-cpp-", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpeechModel(string modelPart)
    {
        string normalized = modelPart.Trim();
        return IsLocalSpeechModel(normalized) ||
               normalized.StartsWith("whisper-large-", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "openai/whisper-1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "openai/gpt-4o-mini-transcribe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "mistralai/voxtral-mini-transcribe", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatModelPart(string modelPart)
    {
        string normalized = modelPart.Trim();

        return normalized switch
        {
            "faster-whisper-base" => "Faster Whisper Base",
            "faster-whisper-base-en-int8" => "Base.en CT2",
            "faster-whisper-small" => "Faster Whisper Small",
            "faster-whisper-small-en-int8" => "Small.en CT2",
            "whisper-cpp-base-en-q8_0" => "Base.en CPP",
            "whisper-cpp-small-en-q8_0" => "Small.en CPP",
            "whisper-large-v3-turbo" => "Whisper Turbo",
            "whisper-large-v3" => "Whisper Large V3",
            "gemini-3.1-flash-lite" => "Gemini 3.1 Flash-Lite",
            "gemini-3.5-flash" => "Gemini 3.5 Flash",
            "gemini-3.1-pro-preview" => "Gemini 3.1 Pro Preview",
            "gemini-flash-lite-latest" => "Gemini Flash Lite",
            "gemini-flash-latest" => "Gemini Flash",
            "gemini-pro-latest" => "Gemini Pro",
            "google/gemini-3.1-flash-lite" => "Gemini 3.1 Flash-Lite",
            "google/gemini-3.5-flash" => "Gemini 3.5 Flash",
            "google/gemini-2.5-flash-lite" => "Gemini Flash Lite",
            "google/gemini-2.5-flash" => "Gemini Flash",
            "openai/gpt-4o-mini-transcribe" => "GPT-4o Mini Transcribe",
            "openai/gpt-5.4-mini" => "GPT-5.4 Mini",
            "openai/gpt-4.1-mini" => "GPT-4.1 Mini",
            "openai/gpt-4.1-nano" => "GPT-4.1 Nano",
            "anthropic/claude-haiku-4.5" => "Claude Haiku 4.5",
            "qwen/qwen3.7-plus" => "Qwen3.7 Plus",
            "deepseek/deepseek-v4-flash" => "DeepSeek V4 Flash",
            "openai/whisper-1" => "Whisper 1",
            "mistralai/mistral-small-2603" => "Mistral Small 4",
            "mistralai/voxtral-mini-transcribe" => "Voxtral Mini Transcribe",
            _ => HumanizeModelId(normalized)
        };
    }

    private static string HumanizeModelId(string modelId)
    {
        string withoutProvider = modelId.Contains('/')
            ? modelId[(modelId.LastIndexOf('/') + 1)..]
            : modelId;

        string words = withoutProvider
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();

        return string.IsNullOrWhiteSpace(words)
            ? modelId
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words.ToLowerInvariant());
    }
}
