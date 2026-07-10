using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using AirType.Services;
using AirType.Services.Configuration;
using AirType.Services.Transcription;
using System.Collections.Generic;
using AirType.Models;
using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Database;
using AirType.Services.Prompts;
using AirType.Views;

namespace AirType.ViewModels;

public class SettingsViewModel : BaseViewModel, IDisposable
{
    private const string AudioSection = "Audio";
    private const string ShortcutsSection = "Shortcuts";
    private const string ProviderSection = "Provider";
    private const string ApiKeysSection = "API Keys";
    private const string PromptsSection = "Prompts";
    private const string PreferencesSection = "General";
    private const string DiagnosticsSection = "Diagnostics";
    private const double OfflineEngineProgressEasingFactor = 0.10;
    private const double OfflineEngineProgressMinimumStep = 0.45;
    private const double OfflineEngineProgressLargeStep = 1.20;

    private readonly ICredentialManager _credentialManager;
    private readonly AppSettingsManager _appSettings;
    private readonly IAudioInputManager _audioInputManager;
    private readonly IHotkeyManager _hotkeyManager;
    private readonly IGeminiApiClient _geminiApiClient;
    private readonly IOpenRouterClient _openRouterClient;
    private readonly IGroqApiClient _groqApiClient;
    private readonly IOfflineEngineManager _offlineEngineManager;
    private readonly ILocalAsrWorkerSupervisor? _localAsrWorkerSupervisor;
    private readonly PromptDatabase _promptDatabase;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private readonly System.Windows.Threading.DispatcherTimer _offlineEngineProgressTimer;
    private static string _lastSelectedSection = AudioSection;
    private string _selectedSection = _lastSelectedSection;
    private bool _disposed;
    private bool _isLoadingSettings;
    private bool _suppressProviderPersistence;
    private bool _suppressModelPersistence;
    private bool _isRefreshingOfflineEngineStatus;
    private bool _isRebuildingProviderOptions;
    private bool _isRebuildingModelOptions;
    private double _offlineEngineInstallTargetProgress;
    private CancellationTokenSource? _offlineEngineInstallCancellation;

    public event EventHandler? SettingsLoaded;
    #region Properties

    // Expander States (all collapsed by default)
    private bool _isAudioExpanded;
    private bool _isShortcutsExpanded;
    private bool _isProviderExpanded;
    private bool _isApiKeysExpanded;
    private bool _isPromptsExpanded;
    private bool _isPreferencesExpanded;
    private bool _isDiagnosticsExpanded;
    private bool _allExpanded;

    // Audio Settings
    public ObservableCollection<AudioDeviceInfo> AudioDevices { get; } = new();

    // Shortcut Settings
    private bool _isHotkeyEditMode;
    private string _currentHotkeyDisplay = "None";
    private string _pendingHotkeyDisplay = string.Empty;
    private Keys _pendingKey;
    private ModifierKeys _pendingModifiers;
    private GlobalKeyboardHook? _keyboardHook;

    public bool IsHotkeyEditMode
    {
        get => _isHotkeyEditMode;
        set => SetProperty(ref _isHotkeyEditMode, value);
    }

    public string CurrentHotkeyDisplay
    {
        get => _currentHotkeyDisplay;
        set => SetProperty(ref _currentHotkeyDisplay, value);
    }

    public string PendingHotkeyDisplay
    {
        get => _pendingHotkeyDisplay;
        set => SetProperty(ref _pendingHotkeyDisplay, value);
    }

    // Provider Settings
    private string _selectedProvider = ProviderModelCatalog.GetAsrDisplayName(TranscriptionProvider.Local);
    public ObservableCollection<string> Providers { get; } = new(
        ProviderModelCatalog.AsrProviderOrder.Select(ProviderModelCatalog.GetAsrDisplayName));
    private string _selectedModel = ProviderModelCatalog.GetDefaultModelId(TranscriptionProvider.Local);
    private string _selectedCleanupProvider = ProviderModelCatalog.GetDisplayName(CleanupProvider.Gemini);
    private string _selectedCleanupModel = ProviderModelCatalog.GetDefaultCleanupModelId(CleanupProvider.Gemini);
    private bool _isTranscriptCleanupEnabled = true;
    public ObservableCollection<string> CleanupProviders { get; } = new(
        ProviderModelCatalog.CleanupProviderOrder.Select(ProviderModelCatalog.GetDisplayName));
    private string _offlineEngineStatus = "Not installed";
    private string _offlineEngineStatusKind = "NotInstalled";
    private string _offlineEngineRecommendation = string.Empty;
    private string _offlineEngineInstallLocation = string.Empty;
    private double _offlineEngineInstallProgress;
    private string _offlineEngineInstallProgressText = string.Empty;
    private bool _isOfflineEngineInstalling;
    private bool _isOfflineEngineRemoving;
    private bool _isOfflineEngineInstalled;
    private bool _hasOfflineEngineInstallSource;

    private string _geminiApiKey = string.Empty;
    private string _openRouterApiKey = string.Empty;
    private string _groqApiKey = string.Empty;

    // Prompts
    private PromptProfile? _selectedProfile;
    public ObservableCollection<PromptProfile> Profiles { get; } = new();
    
    // Prompt Editing Fields
    private string _promptName = string.Empty;
    private string _systemPrompt = string.Empty;
    private bool _isProfileEditable;
    private bool _isNameFieldVisible;
    private bool _isSaveVisible;
    private bool _isDuplicateVisible;
    private bool _isDeleteVisible;
    private bool _isLoadingPrompts;

    // Preferences
    private string _formattingMode = "Plain Text";
    public ObservableCollection<string> FormattingModes { get; } = new() { "Plain Text", "Markdown" };
    private string _selectedCleanupContextMode = CleanupContextMode.Auto.ToString();
    private string _selectedCleanupIntensity = CleanupIntensity.Standard.ToString();
    public ObservableCollection<string> CleanupContextModes { get; } = new()
    {
        CleanupContextMode.Auto.ToString(),
        CleanupContextMode.Terminal.ToString(),
        CleanupContextMode.Editor.ToString(),
        CleanupContextMode.Email.ToString(),
        CleanupContextMode.Chat.ToString(),
        CleanupContextMode.Generic.ToString()
    };
    public ObservableCollection<string> CleanupIntensities { get; } = new()
    {
        CleanupIntensity.Light.ToString(),
        CleanupIntensity.Standard.ToString()
    };
    
    private string _storageRetention = "Never Delete";
    private bool _launchOnStartup;
    private bool _minimizeToTray;
    private bool _muteSystemAudioDuringRecording;
    private bool _enableSmartInsertion = true;
    private bool _enableNativeTypingInjection = true;
    public ObservableCollection<string> RetentionPeriods { get; } = new() 
    { 
        "Never Delete", "7 Days", "30 Days", "90 Days" 
    };

    // Diagnostics
    private string _systemInfo = "Loading system information...";
    private string _averageLatency = "2.45s average";
    private ObservableCollection<double> _latencyData = new();

    #endregion

    #region Commands

    public ICommand ToggleExpandAllCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand StartHotkeyEditCommand { get; }
    public ICommand SaveHotkeyCommand { get; }
    public ICommand CancelHotkeyEditCommand { get; }
    public ICommand ClearHotkeyCommand { get; }
    public ICommand SaveApiKeyCommand { get; }
    public ICommand ClearApiKeyCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand? ProfileChangedCommand { get; }
    public ICommand CreateProfileCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand RefreshLogsCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand CopySystemInfoCommand { get; }
    public ICommand InstallOfflineEngineCommand { get; }
    public ICommand CancelOfflineEngineInstallCommand { get; }
    public ICommand RemoveOfflineEngineCommand { get; }
    public ICommand OpenOfflineEngineFolderCommand { get; }
    public ICommand RefreshOfflineEngineCommand { get; }

    #endregion

    #region Constructor

    public SettingsViewModel(
        ICredentialManager credentialManager, 
        AppSettingsManager appSettings,
        IAudioInputManager audioInputManager,
        IHotkeyManager hotkeyManager,
        IGeminiApiClient geminiApiClient,
        IOpenRouterClient openRouterClient,
        IGroqApiClient groqApiClient,
        IOfflineEngineManager offlineEngineManager,
        PromptDatabase promptDatabase,
        ILocalAsrWorkerSupervisor? localAsrWorkerSupervisor = null)
    {
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _audioInputManager = audioInputManager ?? throw new ArgumentNullException(nameof(audioInputManager));
        _hotkeyManager = hotkeyManager ?? throw new ArgumentNullException(nameof(hotkeyManager));
        _geminiApiClient = geminiApiClient ?? throw new ArgumentNullException(nameof(geminiApiClient));
        _openRouterClient = openRouterClient ?? throw new ArgumentNullException(nameof(openRouterClient));
        _groqApiClient = groqApiClient ?? throw new ArgumentNullException(nameof(groqApiClient));
        _offlineEngineManager = offlineEngineManager ?? throw new ArgumentNullException(nameof(offlineEngineManager));
        _localAsrWorkerSupervisor = localAsrWorkerSupervisor;
        _promptDatabase = promptDatabase ?? throw new ArgumentNullException(nameof(promptDatabase));

        ToggleExpandAllCommand = new RelayCommand(ToggleExpandAll);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);

        StartHotkeyEditCommand = new RelayCommand(StartHotkeyEdit);
        SaveHotkeyCommand = new RelayCommand(SaveHotkey);
        CancelHotkeyEditCommand = new RelayCommand(CancelHotkeyEdit);
        ClearHotkeyCommand = new RelayCommand(ClearHotkey);

        SaveApiKeyCommand = new RelayCommand(SaveApiKeys);
        ClearApiKeyCommand = new RelayCommand<string>(ClearApiKey);
        TestConnectionCommand = new RelayCommand<string>(async (provider) => await TestConnectionAsync(provider));
        // ProfileChangedCommand is handled by property setter
        CreateProfileCommand = new RelayCommand(CreateProfile);
        SaveProfileCommand = new RelayCommand(SaveProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile);
        RefreshLogsCommand = new RelayCommand(ExecuteRefreshLogs);
        CopyLogsCommand = new RelayCommand(CopyLogs);
        ClearLogsCommand = new RelayCommand(ClearLogs);
        CopySystemInfoCommand = new RelayCommand(ExecuteCopySystemInfo);
        InstallOfflineEngineCommand = new RelayCommand(async () => await InstallOfflineEngineAsync());
        CancelOfflineEngineInstallCommand = new RelayCommand(CancelOfflineEngineInstall, () => CanCancelOfflineEngineInstall);
        RemoveOfflineEngineCommand = new RelayCommand(async () => await RemoveOfflineEngineAsync());
        OpenOfflineEngineFolderCommand = new RelayCommand(OpenOfflineEngineFolder);
        RefreshOfflineEngineCommand = new RelayCommand(RefreshOfflineEngineStatus);

        _offlineEngineProgressTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(70)
        };
        _offlineEngineProgressTimer.Tick += OnOfflineEngineProgressTimerTick;

        LoadSettings();
        LoadSystemInfo();
        RefreshDevices();
        LoadPrompts();
        RefreshOfflineEngineStatus();

        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
        
        // Initial refresh for clean start
        RefreshDiagnostics();

        _hotkeyManager.HotkeyChanged += OnHotkeyChanged;
        _hotkeyManager.HotkeyRegistrationFailed += OnHotkeyRegistrationFailed;
    }

    #endregion

    #region Expander Properties

    public bool IsAudioExpanded
    {
        get => _isAudioExpanded;
        set => SetProperty(ref _isAudioExpanded, value);
    }

    public bool IsShortcutsExpanded
    {
        get => _isShortcutsExpanded;
        set => SetProperty(ref _isShortcutsExpanded, value);
    }

    public bool IsProviderExpanded
    {
        get => _isProviderExpanded;
        set => SetProperty(ref _isProviderExpanded, value);
    }

    public bool IsApiKeysExpanded
    {
        get => _isApiKeysExpanded;
        set => SetProperty(ref _isApiKeysExpanded, value);
    }

    public bool IsPromptsExpanded
    {
        get => _isPromptsExpanded;
        set => SetProperty(ref _isPromptsExpanded, value);
    }

    public bool IsPreferencesExpanded
    {
        get => _isPreferencesExpanded;
        set => SetProperty(ref _isPreferencesExpanded, value);
    }

    public bool IsDiagnosticsExpanded
    {
        get => _isDiagnosticsExpanded;
        set => SetProperty(ref _isDiagnosticsExpanded, value);
    }

    public string SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                _lastSelectedSection = value;
                OnPropertyChanged(nameof(IsAudioSectionSelected));
                OnPropertyChanged(nameof(IsShortcutsSectionSelected));
                OnPropertyChanged(nameof(IsProviderSectionSelected));
                OnPropertyChanged(nameof(IsApiKeysSectionSelected));
                OnPropertyChanged(nameof(IsPromptsSectionSelected));
                OnPropertyChanged(nameof(IsPreferencesSectionSelected));
                OnPropertyChanged(nameof(IsDiagnosticsSectionSelected));
            }
        }
    }

    public bool IsAudioSectionSelected => string.Equals(SelectedSection, AudioSection, StringComparison.Ordinal);
    public bool IsShortcutsSectionSelected => string.Equals(SelectedSection, ShortcutsSection, StringComparison.Ordinal);
    public bool IsProviderSectionSelected => string.Equals(SelectedSection, ProviderSection, StringComparison.Ordinal);
    public bool IsApiKeysSectionSelected => string.Equals(SelectedSection, ApiKeysSection, StringComparison.Ordinal);
    public bool IsPromptsSectionSelected => string.Equals(SelectedSection, PromptsSection, StringComparison.Ordinal);
    public bool IsPreferencesSectionSelected => string.Equals(SelectedSection, PreferencesSection, StringComparison.Ordinal);
    public bool IsDiagnosticsSectionSelected => string.Equals(SelectedSection, DiagnosticsSection, StringComparison.Ordinal);

    public string ExpandAllButtonText => _allExpanded ? "Collapse All" : "Expand All";

    #endregion

    #region Audio Properties

    private Models.AudioDeviceInfo? _selectedMicrophone;
    public Models.AudioDeviceInfo? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set
        {
            if (SetProperty(ref _selectedMicrophone, value))
            {
                // If value is null or has DeviceNumber -1 (Default item), set preferred to null (system default)
                // Otherwise set specific device number
                int? deviceNumber = (value == null || value.DeviceNumber == -1) ? null : value.DeviceNumber;
                _audioInputManager.SetPreferredDevice(deviceNumber);
            }
        }
    }

    #endregion

    #region Provider Properties

    public string SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (_isRebuildingProviderOptions || _isRebuildingModelOptions || _isLoadingSettings || _suppressProviderPersistence)
                {
                    return;
                }

                value = ProviderModelCatalog.GetAsrDisplayName(TranscriptionProvider.Groq);
            }

            if (ProviderModelCatalog.TryParseAsrProvider(value, out var requestedProvider) &&
                LocalAsrProviderAvailability.ResolveProvider(
                    requestedProvider,
                    _offlineEngineManager,
                    GetConfiguredModelOrDefault(TranscriptionProvider.Local)) != requestedProvider)
            {
                value = ProviderModelCatalog.GetAsrDisplayName(TranscriptionProvider.Groq);
            }

            if (SetProperty(ref _selectedProvider, value))
            {
                bool shouldPersist = !_isLoadingSettings && !_suppressProviderPersistence;
                TranscriptionProvider? providerToPersist = null;

                if (ProviderModelCatalog.TryParseAsrProvider(value, out var provider))
                {
                    providerToPersist = provider;
                    string targetModel = GetConfiguredModelOrDefault(provider);
                    UpdateAvailableModels(targetModel);
                    if (!_isRefreshingOfflineEngineStatus)
                    {
                        RefreshOfflineEngineStatus();
                    }
                }
                else
                {
                    UpdateAvailableModels();
                    if (!_isRefreshingOfflineEngineStatus)
                    {
                        RefreshOfflineEngineStatus();
                    }
                }

                if (shouldPersist && providerToPersist.HasValue)
                {
                    try
                    {
                        _credentialManager.SaveActiveProvider(providerToPersist.Value);
                        if (!string.IsNullOrWhiteSpace(SelectedModel))
                        {
                            _credentialManager.SetActiveModel(providerToPersist.Value, SelectedModel);
                        }

                        SynchronizeLocalAsrWorkerForSelection();
                        Logger.Debug("SettingsViewModel", $"Auto-saved provider: {value}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("SettingsViewModel", $"Failed to auto-save provider: {ex.Message}", ex);
                    }
                }
            }
        }
    }

    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (_isRebuildingModelOptions || _isLoadingSettings || _suppressModelPersistence)
                {
                    return;
                }

                if (ProviderModelCatalog.TryParseAsrProvider(SelectedProvider, out var fallbackProvider))
                {
                    value = GetConfiguredModelOrDefault(fallbackProvider);
                }
                else
                {
                    return;
                }
            }

            if (SetProperty(ref _selectedModel, value))
            {
                if (_isLoadingSettings || _suppressModelPersistence)
                {
                    return;
                }

                // Auto-save model selection
                if (ProviderModelCatalog.TryParseAsrProvider(SelectedProvider, out var provider))
                {
                    if (!ProviderModelCatalog.ContainsModel(provider, value))
                    {
                        Logger.Debug("SettingsViewModel", $"Ignoring unsupported transient model value '{value}' for {SelectedProvider}.");
                        return;
                    }

                    try
                    {
                        _credentialManager.SetActiveModel(provider, value);
                        Logger.Debug("SettingsViewModel", $"Auto-saved model: {value} for {SelectedProvider}");
                        if (provider == TranscriptionProvider.Local)
                        {
                            RefreshOfflineEngineStatus();
                        }

                        SynchronizeLocalAsrWorkerForSelection();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("SettingsViewModel", $"Failed to auto-save model: {ex.Message}", ex);
                    }
                }
            }
        }
    }

    public ObservableCollection<ModelInfo> AvailableModels { get; } = new();

    public string OfflineEngineStatus
    {
        get => _offlineEngineStatus;
        set => SetProperty(ref _offlineEngineStatus, value);
    }

    public string OfflineEngineStatusKind
    {
        get => _offlineEngineStatusKind;
        set
        {
            if (SetProperty(ref _offlineEngineStatusKind, value))
            {
                OnPropertyChanged(nameof(OfflineEngineInstallActionText));
                OnPropertyChanged(nameof(ShowOfflineEngineInstallAction));
                OnPropertyChanged(nameof(ShowOfflineEngineRemoveAction));
                OnPropertyChanged(nameof(ShowOfflineEngineFolderAction));
            }
        }
    }

    public string OfflineEngineRecommendation
    {
        get => _offlineEngineRecommendation;
        set => SetProperty(ref _offlineEngineRecommendation, value);
    }

    public string OfflineEngineInstallLocation
    {
        get => _offlineEngineInstallLocation;
        set => SetProperty(ref _offlineEngineInstallLocation, value);
    }

    public double OfflineEngineInstallProgress
    {
        get => _offlineEngineInstallProgress;
        set => SetProperty(ref _offlineEngineInstallProgress, value);
    }

    public string OfflineEngineInstallProgressText
    {
        get => _offlineEngineInstallProgressText;
        set => SetProperty(ref _offlineEngineInstallProgressText, value);
    }

    public bool IsOfflineEngineInstalling
    {
        get => _isOfflineEngineInstalling;
        set
        {
            if (SetProperty(ref _isOfflineEngineInstalling, value))
            {
                OnPropertyChanged(nameof(CanUseOfflineEngineActions));
                OnPropertyChanged(nameof(CanInstallOfflineEngine));
                OnPropertyChanged(nameof(CanCancelOfflineEngineInstall));
                OnPropertyChanged(nameof(ShowOfflineEngineCancelAction));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsOfflineEngineRemoving
    {
        get => _isOfflineEngineRemoving;
        set
        {
            if (SetProperty(ref _isOfflineEngineRemoving, value))
            {
                OnPropertyChanged(nameof(CanUseOfflineEngineActions));
                OnPropertyChanged(nameof(CanInstallOfflineEngine));
                OnPropertyChanged(nameof(ShowOfflineEngineInstallAction));
                OnPropertyChanged(nameof(ShowOfflineEngineRemoveAction));
                OnPropertyChanged(nameof(ShowOfflineEngineFolderAction));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsOfflineEngineInstalled
    {
        get => _isOfflineEngineInstalled;
        set
        {
            if (SetProperty(ref _isOfflineEngineInstalled, value))
            {
                OnPropertyChanged(nameof(CanInstallOfflineEngine));
                OnPropertyChanged(nameof(OfflineEngineInstallActionText));
                OnPropertyChanged(nameof(ShowOfflineEngineInstallAction));
                OnPropertyChanged(nameof(ShowOfflineEngineRemoveAction));
                OnPropertyChanged(nameof(ShowOfflineEngineFolderAction));
            }
        }
    }

    public bool CanUseOfflineEngineActions => !IsOfflineEngineInstalling && !IsOfflineEngineRemoving;

    public bool CanInstallOfflineEngine => !IsOfflineEngineInstalling && !IsOfflineEngineRemoving && _hasOfflineEngineInstallSource && !IsOfflineEngineInstalled;

    public bool CanCancelOfflineEngineInstall => IsOfflineEngineInstalling;

    public string OfflineEngineInstallActionText =>
        OfflineEngineStatusKind == "NeedsAttention" ? "Reinstall" : "Download";

    public bool ShowOfflineEngineInstallAction => !IsOfflineEngineInstalled && !IsOfflineEngineInstalling && !IsOfflineEngineRemoving;

    public bool ShowOfflineEngineCancelAction => IsOfflineEngineInstalling;

    public bool ShowOfflineEngineRemoveAction =>
        !IsOfflineEngineInstalling &&
        !IsOfflineEngineRemoving &&
        (IsOfflineEngineInstalled || OfflineEngineStatusKind == "NeedsAttention");

    public bool ShowOfflineEngineFolderAction =>
        !IsOfflineEngineInstalling &&
        !IsOfflineEngineRemoving &&
        (IsOfflineEngineInstalled || OfflineEngineStatusKind == "NeedsAttention");

    public string SelectedCleanupProvider
    {
        get => _selectedCleanupProvider;
        set
        {
            if (SetProperty(ref _selectedCleanupProvider, value))
            {
                bool shouldPersist = !_isLoadingSettings && !_suppressProviderPersistence;
                if (ProviderModelCatalog.TryParseCleanupProvider(value, out var provider))
                {
                    string targetModel = _credentialManager.GetCleanupModelId(provider);
                    UpdateAvailableCleanupModels(targetModel);

                    if (shouldPersist)
                    {
                        try
                        {
                            _credentialManager.SaveActiveCleanupProvider(provider);
                            if (!string.IsNullOrWhiteSpace(SelectedCleanupModel))
                            {
                                _credentialManager.SetActiveCleanupModel(provider, SelectedCleanupModel);
                            }

                            Logger.Debug("SettingsViewModel", $"Auto-saved cleanup provider: {value}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("SettingsViewModel", $"Failed to auto-save cleanup provider: {ex.Message}", ex);
                        }
                    }
                }
                else
                {
                    UpdateAvailableCleanupModels();
                }
            }
        }
    }

    public string SelectedCleanupModel
    {
        get => _selectedCleanupModel;
        set
        {
            if (SetProperty(ref _selectedCleanupModel, value))
            {
                if (_isLoadingSettings || _suppressModelPersistence)
                {
                    return;
                }

                if (ProviderModelCatalog.TryParseCleanupProvider(SelectedCleanupProvider, out var provider))
                {
                    try
                    {
                        _credentialManager.SetActiveCleanupModel(provider, value);
                        Logger.Debug("SettingsViewModel", $"Auto-saved cleanup model: {value} for {SelectedCleanupProvider}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("SettingsViewModel", $"Failed to auto-save cleanup model: {ex.Message}", ex);
                    }
                }
            }
        }
    }

    public ObservableCollection<ModelInfo> AvailableCleanupModels { get; } = new();

    public bool IsTranscriptCleanupEnabled
    {
        get => _isTranscriptCleanupEnabled;
        set
        {
            if (SetProperty(ref _isTranscriptCleanupEnabled, value))
            {
                OnPropertyChanged(nameof(CanUseCleanupSelection));

                if (_isLoadingSettings)
                {
                    return;
                }

                try
                {
                    _credentialManager.SetTranscriptCleanupEnabled(value);
                    Logger.Debug("SettingsViewModel", $"Auto-saved transcript cleanup enabled: {value}");
                }
                catch (Exception ex)
                {
                    Logger.Error("SettingsViewModel", $"Failed to auto-save transcript cleanup setting: {ex.Message}", ex);
                }
            }
        }
    }

    public bool CanUseCleanupSelection => IsTranscriptCleanupEnabled;

    #endregion

    #region API Keys Properties

    public string GeminiApiKey
    {
        get => _geminiApiKey;
        set
        {
            if (SetProperty(ref _geminiApiKey, value))
            {
                GeminiStatus = string.IsNullOrWhiteSpace(value) 
                    ? ApiKeyStatus.NotConfigured 
                    : ApiKeyStatus.Configured;
            }
        }
    }

    public string OpenRouterApiKey
    {
        get => _openRouterApiKey;
        set
        {
            if (SetProperty(ref _openRouterApiKey, value))
            {
                OpenRouterStatus = string.IsNullOrWhiteSpace(value)
                    ? ApiKeyStatus.NotConfigured
                    : ApiKeyStatus.Configured;
            }
        }
    }

    public string GroqApiKey
    {
        get => _groqApiKey;
        set
        {
            if (SetProperty(ref _groqApiKey, value))
            {
                GroqStatus = string.IsNullOrWhiteSpace(value)
                    ? ApiKeyStatus.NotConfigured
                    : ApiKeyStatus.Configured;
            }
        }
    }

    private ApiKeyStatus _geminiStatus;
    public ApiKeyStatus GeminiStatus
    {
        get => _geminiStatus;
        set => SetProperty(ref _geminiStatus, value);
    }

    private ApiKeyStatus _openRouterStatus;
    public ApiKeyStatus OpenRouterStatus
    {
        get => _openRouterStatus;
        set => SetProperty(ref _openRouterStatus, value);
    }

    private ApiKeyStatus _groqStatus;
    public ApiKeyStatus GroqStatus
    {
        get => _groqStatus;
        set => SetProperty(ref _groqStatus, value);
    }

    #endregion

    #region Prompts Properties

    public PromptProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                if (_selectedProfile != null)
                {
                    // "Create New..." Logic (Id == -1 and IsBuiltIn == true, but using name check to be safe)
                    bool isCreateNew = _selectedProfile.Name == "Create New...";
                    
                    if (isCreateNew)
                    {
                        PromptName = "New Custom Prompt";
                        SystemPrompt = "Enter system instruction here...";
                        IsProfileEditable = true;
                        IsNameFieldVisible = true;
                        
                        IsSaveVisible = true;       // Save "New"
                        IsDuplicateVisible = false;
                        IsDeleteVisible = false;
                    }
                    else
                    {
                        PromptName = _selectedProfile.Name;

                        // Custom profiles are editable (content only), Built-in are not
                        IsProfileEditable = IsCustomPromptProfile(_selectedProfile);

                        // Hide built-in prompt content from the user; custom prompts show normally
                        SystemPrompt = IsCustomPromptProfile(_selectedProfile) ? _selectedProfile.Content : "";

                        // Name hidden for existing profiles
                        IsNameFieldVisible = false;

                        // Buttons for Custom vs Built-in
                        if (!IsCustomPromptProfile(_selectedProfile))
                        {
                            IsSaveVisible = false;
                            IsDuplicateVisible = false;
                            IsDeleteVisible = false;
                        }
                        else
                        {
                            IsSaveVisible = true;       // "Save Changes"
                            IsDuplicateVisible = true;
                            IsDeleteVisible = true;
                        }

                        PersistSelectedStyleOverride(_selectedProfile);
                    }
                }
            }
        }
    }

    public string PromptName
    {
        get => _promptName;
        set => SetProperty(ref _promptName, value);
    }

    public string SystemPrompt
    {
        get => _systemPrompt;
        set => SetProperty(ref _systemPrompt, value);
    }

    public bool IsProfileEditable
    {
        get => _isProfileEditable;
        set => SetProperty(ref _isProfileEditable, value);
    }

    public bool IsNameFieldVisible
    {
        get => _isNameFieldVisible;
        set => SetProperty(ref _isNameFieldVisible, value);
    }

    public bool IsSaveVisible
    {
        get => _isSaveVisible;
        set => SetProperty(ref _isSaveVisible, value);
    }

    public bool IsDuplicateVisible
    {
        get => _isDuplicateVisible;
        set => SetProperty(ref _isDuplicateVisible, value);
    }

    public bool IsDeleteVisible
    {
        get => _isDeleteVisible;
        set => SetProperty(ref _isDeleteVisible, value);
    }

    #endregion

    #region Preference Properties

    public string FormattingMode
    {
        get => _formattingMode;
        set
        {
            if (SetProperty(ref _formattingMode, value) && !_isLoadingSettings)
            {
                _credentialManager.SetTextFormattingMode(
                    string.Equals(value, "Markdown", StringComparison.Ordinal)
                        ? TextFormattingMode.Markdown
                        : TextFormattingMode.PlainText);
            }
        }
    }

    public string SelectedCleanupContextMode
    {
        get => _selectedCleanupContextMode;
        set
        {
            if (SetProperty(ref _selectedCleanupContextMode, value) && !_isLoadingSettings)
            {
                if (Enum.TryParse<CleanupContextMode>(value, out var mode))
                {
                    _credentialManager.SetCleanupContextMode(mode);
                }
            }
        }
    }

    public string SelectedCleanupIntensity
    {
        get => _selectedCleanupIntensity;
        set
        {
            if (SetProperty(ref _selectedCleanupIntensity, value) && !_isLoadingSettings)
            {
                if (Enum.TryParse<CleanupIntensity>(value, out var intensity))
                {
                    _credentialManager.SetCleanupIntensity(intensity);
                }
            }
        }
    }

    public string StorageRetention
    {
        get => _storageRetention;
        set => SetProperty(ref _storageRetention, value);
    }

    public bool LaunchOnStartup
    {
        get => _launchOnStartup;
        set
        {
            if (SetProperty(ref _launchOnStartup, value))
            {
                _appSettings.LaunchOnStartup = value;
            }
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (SetProperty(ref _minimizeToTray, value))
            {
                _appSettings.MinimizeToTray = value;
            }
        }
    }

    public bool MuteSystemAudioDuringRecording
    {
        get => _muteSystemAudioDuringRecording;
        set
        {
            if (SetProperty(ref _muteSystemAudioDuringRecording, value))
            {
                _appSettings.MuteSystemAudioDuringRecording = value;
            }
        }
    }

    public bool EnableSmartInsertion
    {
        get => _enableSmartInsertion;
        set
        {
            if (SetProperty(ref _enableSmartInsertion, value))
            {
                _appSettings.EnableSmartInsertion = value;
            }
        }
    }

    public bool EnableNativeTypingInjection
    {
        get => _enableNativeTypingInjection;
        set
        {
            if (SetProperty(ref _enableNativeTypingInjection, value))
            {
                _appSettings.EnableNativeTypingInjection = value;
            }
        }
    }

    #endregion

    #region Diagnostics Properties

    public string SystemInfo
    {
        get => _systemInfo;
        set => SetProperty(ref _systemInfo, value);
    }

    public string AverageLatency
    {
        get => _averageLatency;
        set => SetProperty(ref _averageLatency, value);
    }

    public ObservableCollection<double> LatencyData
    {
        get => _latencyData;
        set => SetProperty(ref _latencyData, value);
    }

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    #endregion

    #region Methods

    private void ToggleExpandAll()
    {
        _allExpanded = !_allExpanded;
        OnPropertyChanged(nameof(ExpandAllButtonText));

        IsAudioExpanded = _allExpanded;
        IsShortcutsExpanded = _allExpanded;
        IsProviderExpanded = _allExpanded;
        IsApiKeysExpanded = _allExpanded;
        IsPromptsExpanded = _allExpanded;
        IsPreferencesExpanded = _allExpanded;
        IsDiagnosticsExpanded = _allExpanded;
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        RefreshDiagnostics();
    }

    private void OnHotkeyChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentHotkeyDisplay = FormatHotkeyText(_hotkeyManager.RegisteredKey, _hotkeyManager.RegisteredModifiers);
        });
    }

    private void OnHotkeyRegistrationFailed(object? sender, HotkeyRegistrationFailedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            NotificationDialog.ShowWarning("Hotkey Error", $"Failed to register hotkey: {e.ErrorMessage}", centerOnContentArea: true);
            CurrentHotkeyDisplay = FormatHotkeyText(_hotkeyManager.RegisteredKey, _hotkeyManager.RegisteredModifiers);
        });
    }

    private void RefreshOfflineEngineStatus()
    {
        if (_isRefreshingOfflineEngineStatus || IsOfflineEngineRemoving)
        {
            return;
        }

        _isRefreshingOfflineEngineStatus = true;
        try
        {
            var status = _offlineEngineManager.GetStatus(GetSelectedLocalModelId());
            OfflineEngineStatus = status.StatusText;
            OfflineEngineStatusKind = GetOfflineEngineStatusKind(status);
            OfflineEngineRecommendation = status.Recommendation;
            OfflineEngineInstallLocation = status.ModelPath;
            IsOfflineEngineInstalled = status.IsInstalled;
            _hasOfflineEngineInstallSource = status.CanInstall;
            UpdateAsrProviderAvailability();
            OnPropertyChanged(nameof(CanInstallOfflineEngine));
            OnPropertyChanged(nameof(OfflineEngineInstallActionText));
            OnPropertyChanged(nameof(ShowOfflineEngineInstallAction));
            OnPropertyChanged(nameof(ShowOfflineEngineRemoveAction));
            OnPropertyChanged(nameof(ShowOfflineEngineFolderAction));

            if (!IsOfflineEngineInstalling)
            {
                SetOfflineEngineInstallProgressImmediate(
                    status.IsInstalled ? 100 : 0,
                    status.IsInstalled
                    ? "Ready"
                    : status.CanInstall
                        ? "Ready to install"
                        : "Local model package unavailable");
            }
        }
        finally
        {
            _isRefreshingOfflineEngineStatus = false;
        }
    }

    private void SynchronizeLocalAsrWorkerForSelection()
    {
        if (_localAsrWorkerSupervisor == null || _isLoadingSettings)
        {
            return;
        }

        if (!ProviderModelCatalog.TryParseAsrProvider(SelectedProvider, out var provider) ||
            provider != TranscriptionProvider.Local)
        {
            _ = ShutdownLocalAsrWorkerAsync("speech provider changed away from Local");
            return;
        }

        string modelId = GetSelectedLocalModelId();
        var status = _offlineEngineManager.GetStatus(modelId);
        if (!status.IsInstalled)
        {
            _ = ShutdownLocalAsrWorkerAsync($"selected local model is not installed ({status.StatusText})");
            return;
        }

        var model = LocalAsrModelCatalog.GetRequired(modelId);
        _ = PrewarmLocalAsrWorkerAsync(model.DisplayName);
    }

    private async Task PrewarmLocalAsrWorkerAsync(string modelDisplayName)
    {
        try
        {
            Logger.Info("SettingsViewModel", $"Prewarming local ASR model after selection: {modelDisplayName}");
            await _localAsrWorkerSupervisor!.PrewarmActiveModelAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Warn("SettingsViewModel", $"Local ASR prewarm after selection failed: {ex.Message}");
        }
    }

    private async Task ShutdownLocalAsrWorkerAsync(string reason)
    {
        try
        {
            Logger.Info("SettingsViewModel", $"Stopping CT2 local ASR worker because {reason}.");
            await _localAsrWorkerSupervisor!.ShutdownAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Warn("SettingsViewModel", $"Failed to stop CT2 local ASR worker after selection change: {ex.Message}");
        }
    }

    private void UpdateAsrProviderAvailability()
    {
        string localModelId = GetConfiguredModelOrDefault(TranscriptionProvider.Local);
        bool isLocalInstalled = LocalAsrProviderAvailability.IsLocalModelInstalled(_offlineEngineManager, localModelId);
        var availableProviders = LocalAsrProviderAvailability.GetSelectableProviders(isLocalInstalled);
        var providerNames = availableProviders
            .Select(ProviderModelCatalog.GetAsrDisplayName)
            .ToArray();

        bool selectedProviderIsAvailable =
            ProviderModelCatalog.TryParseAsrProvider(SelectedProvider, out var selectedProvider) &&
            availableProviders.Contains(selectedProvider);

        if (!selectedProviderIsAvailable)
        {
            SelectCloudBecauseLocalIsUnavailable(shouldPersist: !_isLoadingSettings && !_suppressProviderPersistence);
            selectedProvider = TranscriptionProvider.Groq;
        }

        SyncProviderOptions(providerNames);

        string selectedProviderDisplayName = ProviderModelCatalog.GetAsrDisplayName(selectedProvider);
        if (!string.Equals(SelectedProvider, selectedProviderDisplayName, StringComparison.Ordinal))
        {
            bool oldSuppressProviderPersistence = _suppressProviderPersistence;
            _suppressProviderPersistence = true;
            try
            {
                SelectedProvider = selectedProviderDisplayName;
            }
            finally
            {
                _suppressProviderPersistence = oldSuppressProviderPersistence;
            }
        }

        OnPropertyChanged(nameof(SelectedProvider));
    }

    private void SyncProviderOptions(string[] providerNames)
    {
        if (Providers.SequenceEqual(providerNames))
        {
            return;
        }

        bool oldSuppressProviderPersistence = _suppressProviderPersistence;
        _suppressProviderPersistence = true;
        _isRebuildingProviderOptions = true;
        try
        {
            for (int i = Providers.Count - 1; i >= 0; i--)
            {
                if (!providerNames.Contains(Providers[i], StringComparer.Ordinal))
                {
                    Providers.RemoveAt(i);
                }
            }

            for (int targetIndex = 0; targetIndex < providerNames.Length; targetIndex++)
            {
                string providerName = providerNames[targetIndex];
                int currentIndex = IndexOfProviderOption(providerName);
                if (currentIndex < 0)
                {
                    Providers.Insert(targetIndex, providerName);
                }
                else if (currentIndex != targetIndex)
                {
                    Providers.Move(currentIndex, targetIndex);
                }
            }
        }
        finally
        {
            _isRebuildingProviderOptions = false;
            _suppressProviderPersistence = oldSuppressProviderPersistence;
        }

        OnPropertyChanged(nameof(Providers));
    }

    private int IndexOfProviderOption(string providerName)
    {
        for (int i = 0; i < Providers.Count; i++)
        {
            if (string.Equals(Providers[i], providerName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void SelectCloudBecauseLocalIsUnavailable(bool shouldPersist)
    {
        string cloudProvider = ProviderModelCatalog.GetAsrDisplayName(TranscriptionProvider.Groq);
        bool oldSuppressProviderPersistence = _suppressProviderPersistence;
        _suppressProviderPersistence = true;
        try
        {
            SelectedProvider = cloudProvider;
        }
        finally
        {
            _suppressProviderPersistence = oldSuppressProviderPersistence;
        }

        if (shouldPersist)
        {
            _credentialManager.SaveActiveProvider(TranscriptionProvider.Groq);
            SynchronizeLocalAsrWorkerForSelection();
        }
    }

    private string GetConfiguredModelOrDefault(TranscriptionProvider provider)
    {
        try
        {
            string configuredModel = _credentialManager.GetModelId(provider);
            if (ProviderModelCatalog.ContainsModel(provider, configuredModel))
            {
                return configuredModel;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("SettingsViewModel", $"Failed to load configured model for {provider}: {ex.Message}");
        }

        return ProviderModelCatalog.GetDefaultModelId(provider);
    }

    private string GetSelectedLocalModelId()
    {
        if (ProviderModelCatalog.TryParseAsrProvider(SelectedProvider, out var provider) &&
            provider == TranscriptionProvider.Local &&
            ProviderModelCatalog.ContainsModel(TranscriptionProvider.Local, SelectedModel))
        {
            return SelectedModel;
        }

        return GetConfiguredModelOrDefault(TranscriptionProvider.Local);
    }

    private string GetSelectedLocalModelDisplayName()
    {
        var model = ProviderModelCatalog.FindModel(TranscriptionProvider.Local, GetSelectedLocalModelId());
        return model?.DisplayName ?? "selected local model";
    }

    private static string GetOfflineEngineStatusKind(OfflineEngineStatus status)
    {
        if (status.IsInstalled)
        {
            return "Installed";
        }

        return string.Equals(status.StatusText, "Not installed", StringComparison.OrdinalIgnoreCase)
            ? "NotInstalled"
            : "NeedsAttention";
    }

    private async Task InstallOfflineEngineAsync()
    {
        if (IsOfflineEngineInstalling)
        {
            return;
        }

        try
        {
            _offlineEngineInstallCancellation?.Dispose();
            _offlineEngineInstallCancellation = new CancellationTokenSource();
            IsOfflineEngineInstalling = true;
            OfflineEngineStatus = "Installing";
            OfflineEngineStatusKind = "NeedsAttention";
            SetOfflineEngineInstallProgressImmediate(0, "Preparing install");

            var progress = new Progress<OfflineEngineInstallProgress>(update =>
            {
                ReportOfflineEngineInstallProgress(update);
            });

            string modelId = GetSelectedLocalModelId();
            var installToken = _offlineEngineInstallCancellation.Token;
            var result = await Task.Run(
                () => _offlineEngineManager.InstallModelAsync(modelId, progress, installToken),
                installToken);
            if (result.Success)
            {
                SetOfflineEngineInstallProgressImmediate(100, "Ready");
                IsOfflineEngineInstalling = false;
                RefreshOfflineEngineStatus();
                CommandManager.InvalidateRequerySuggested();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Render);
                NotificationDialog.ShowSuccess("Offline Engine", result.Message, centerOnContentArea: true);
            }
            else
            {
                NotificationDialog.ShowWarning("Offline Engine", result.Message, centerOnContentArea: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SettingsViewModel", $"Offline engine install failed: {ex.Message}", ex);
            NotificationDialog.ShowError("Offline Engine", $"Offline engine install failed: {ex.Message}", centerOnContentArea: true);
        }
        finally
        {
            IsOfflineEngineInstalling = false;
            _offlineEngineInstallCancellation?.Dispose();
            _offlineEngineInstallCancellation = null;
            RefreshOfflineEngineStatus();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void CancelOfflineEngineInstall()
    {
        if (!IsOfflineEngineInstalling)
        {
            return;
        }

        OfflineEngineInstallProgressText = "Canceling install";
        _offlineEngineInstallCancellation?.Cancel();
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetOfflineEngineInstallProgressImmediate(double percent, string text)
    {
        _offlineEngineProgressTimer.Stop();
        _offlineEngineInstallTargetProgress = Math.Clamp(percent, 0, 100);
        OfflineEngineInstallProgress = _offlineEngineInstallTargetProgress;
        OfflineEngineInstallProgressText = text;
    }

    private void ReportOfflineEngineInstallProgress(OfflineEngineInstallProgress update)
    {
        OfflineEngineInstallProgressText = update.Message;
        double target = Math.Clamp(update.Percent, 0, 100);
        _offlineEngineInstallTargetProgress = Math.Max(_offlineEngineInstallTargetProgress, target);

        if (OfflineEngineInstallProgress >= _offlineEngineInstallTargetProgress)
        {
            OfflineEngineInstallProgress = _offlineEngineInstallTargetProgress;
            return;
        }

        if (!_offlineEngineProgressTimer.IsEnabled)
        {
            _offlineEngineProgressTimer.Start();
        }
    }

    private void OnOfflineEngineProgressTimerTick(object? sender, EventArgs e)
    {
        double remaining = _offlineEngineInstallTargetProgress - OfflineEngineInstallProgress;
        if (remaining <= 0.05)
        {
            OfflineEngineInstallProgress = _offlineEngineInstallTargetProgress;
            if (!IsOfflineEngineInstalling || _offlineEngineInstallTargetProgress >= 100)
            {
                _offlineEngineProgressTimer.Stop();
            }

            return;
        }

        double step = Math.Max(OfflineEngineProgressMinimumStep, remaining * OfflineEngineProgressEasingFactor);
        if (remaining > 12)
        {
            step = Math.Max(step, OfflineEngineProgressLargeStep);
        }

        OfflineEngineInstallProgress = Math.Min(_offlineEngineInstallTargetProgress, OfflineEngineInstallProgress + step);
    }

    private async Task RemoveOfflineEngineAsync()
    {
        if (IsOfflineEngineInstalling || IsOfflineEngineRemoving)
        {
            return;
        }

        if (!ConfirmationDialog.ShowDestructive(
            "Remove Local Model",
            $"Remove {GetSelectedLocalModelDisplayName()} from this device?",
            confirmText: "Remove",
            cancelText: "Cancel",
            centerOnContentArea: true))
        {
            return;
        }

        try
        {
            IsOfflineEngineRemoving = true;
            OfflineEngineStatus = "Removing";
            OfflineEngineStatusKind = "NeedsAttention";
            OnPropertyChanged(nameof(ShowOfflineEngineInstallAction));
            OnPropertyChanged(nameof(ShowOfflineEngineRemoveAction));
            OnPropertyChanged(nameof(ShowOfflineEngineFolderAction));

            SelectCloudForOfflineEngineRemoval();
            await ShutdownLocalAsrWorkerAsync("offline engine is being removed");
            await Task.Run(() => _offlineEngineManager.RemoveAsync());
            NotificationDialog.ShowSuccess("Offline Engine", "Local offline engine removed.", centerOnContentArea: true);
        }
        catch (Exception ex)
        {
            Logger.Error("SettingsViewModel", $"Offline engine removal failed: {ex.Message}", ex);
            NotificationDialog.ShowError("Offline Engine", $"Offline engine removal failed: {ex.Message}", centerOnContentArea: true);
        }
        finally
        {
            IsOfflineEngineRemoving = false;
            RefreshOfflineEngineStatus();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void SelectCloudForOfflineEngineRemoval()
    {
        string cloudProvider = ProviderModelCatalog.GetAsrDisplayName(TranscriptionProvider.Groq);
        bool oldSuppressProviderPersistence = _suppressProviderPersistence;
        _suppressProviderPersistence = true;
        try
        {
            SelectedProvider = cloudProvider;
        }
        finally
        {
            _suppressProviderPersistence = oldSuppressProviderPersistence;
        }

        _credentialManager.SaveActiveProvider(TranscriptionProvider.Groq);
        if (!string.IsNullOrWhiteSpace(SelectedModel))
        {
            _credentialManager.SetActiveModel(TranscriptionProvider.Groq, SelectedModel);
        }
    }

    private void OpenOfflineEngineFolder()
    {
        try
        {
            Directory.CreateDirectory(_offlineEngineManager.InstallDirectory);
            string folderPath = _offlineEngineManager.GetModelFolderPath(GetSelectedLocalModelId());
            if (!Directory.Exists(folderPath))
            {
                folderPath = _offlineEngineManager.InstallDirectory;
            }

            Directory.CreateDirectory(folderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("SettingsViewModel", $"Failed to open offline engine folder: {ex.Message}", ex);
            NotificationDialog.ShowError("Offline Engine", $"Failed to open folder: {ex.Message}", centerOnContentArea: true);
        }
    }

    private void UpdateAvailableModels(string? preferredModelId = null)
    {
        var previousModel = preferredModelId ?? SelectedModel;
        bool oldSuppressModelPersistence = _suppressModelPersistence;
        _suppressModelPersistence = true;
        _isRebuildingModelOptions = true;
        try
        {
            AvailableModels.Clear();

            if (ProviderModelCatalog.TryParseAsrProvider(SelectedProvider, out var provider))
            {
                foreach (var model in ProviderModelCatalog.GetModels(provider))
                {
                    AvailableModels.Add(model);
                }
            }

            string? selectedModel = null;
            if (AvailableModels.Any(model => model.Id == previousModel))
            {
                selectedModel = previousModel;
            }
            else if (AvailableModels.Count > 0)
            {
                selectedModel = AvailableModels[0].Id;
            }

            if (selectedModel != null)
            {
                SelectedModel = selectedModel;
            }
        }
        finally
        {
            _isRebuildingModelOptions = false;
            _suppressModelPersistence = oldSuppressModelPersistence;
        }

        OnPropertyChanged(nameof(AvailableModels));
    }

    private void UpdateAvailableCleanupModels(string? preferredModelId = null)
    {
        var previousModel = preferredModelId ?? SelectedCleanupModel;
        bool oldSuppressModelPersistence = _suppressModelPersistence;
        _suppressModelPersistence = true;
        try
        {
            AvailableCleanupModels.Clear();

            if (ProviderModelCatalog.TryParseCleanupProvider(SelectedCleanupProvider, out var provider))
            {
                foreach (var model in ProviderModelCatalog.GetCleanupModels(provider))
                {
                    AvailableCleanupModels.Add(model);
                }
            }

            string? selectedModel = null;
            if (AvailableCleanupModels.Any(model => model.Id == previousModel))
            {
                selectedModel = previousModel;
            }
            else if (AvailableCleanupModels.Count > 0)
            {
                selectedModel = AvailableCleanupModels[0].Id;
            }

            if (selectedModel != null)
            {
                SelectedCleanupModel = selectedModel;
            }
        }
        finally
        {
            _suppressModelPersistence = oldSuppressModelPersistence;
        }

        OnPropertyChanged(nameof(AvailableCleanupModels));
    }

    private void RefreshDevices()
    {
        AudioDevices.Clear();
        
        // Add Default option (ID -1 will clear preferred device in setter logic)
        AudioDevices.Add(new Models.AudioDeviceInfo(-1, "Default", 0));

        var devices = _audioInputManager.GetAvailableDevices();
        foreach (var device in devices)
        {
            AudioDevices.Add(device);
        }

        // Determine currently selected device
        int? preferred = _audioInputManager.PreferredDeviceNumber;
        if (preferred.HasValue)
        {
            SelectedMicrophone = AudioDevices.FirstOrDefault(d => d.DeviceNumber == preferred.Value) 
                               ?? AudioDevices.FirstOrDefault(d => d.DeviceNumber == -1); // Fallback to default item
        }
        else
        {
            SelectedMicrophone = AudioDevices.FirstOrDefault(d => d.DeviceNumber == -1);
        }
        OnPropertyChanged(nameof(SelectedMicrophone));
    }

    private void LoadPrompts()
    {
        _isLoadingPrompts = true;
        try
        {
            Profiles.Clear();
            Profiles.Add(CreateNoStyleOverrideProfile());
            Profiles.Add(CreateClassicStyleOverrideProfile());

            var prompts = _promptDatabase.GetAllPrompts()
                .Where(prompt => !prompt.IsBuiltIn)
                .ToList();
            foreach (var prompt in prompts)
            {
                Profiles.Add(prompt);
            }

            // Add "Create New..." option at the end
            Profiles.Add(PromptProfile.CreateNew);

            SelectedProfile = ResolveSelectedStyleOverrideProfile()
                              ?? Profiles.FirstOrDefault();
        }
        finally
        {
            _isLoadingPrompts = false;
        }
    }

    private PromptProfile? ResolveSelectedStyleOverrideProfile()
    {
        return _credentialManager.GetCleanupStyleOverrideKind() switch
        {
            CleanupStyleOverrideKind.Classic =>
                Profiles.FirstOrDefault(IsClassicStyleOverrideProfile),
            CleanupStyleOverrideKind.CustomPrompt =>
                Profiles.FirstOrDefault(profile =>
                    IsCustomPromptProfile(profile) &&
                    profile.Id == _credentialManager.GetCleanupStylePromptId()),
            _ => Profiles.FirstOrDefault(IsNoStyleOverrideProfile)
        };
    }

    private void PersistSelectedStyleOverride(PromptProfile profile)
    {
        if (_isLoadingPrompts || _isLoadingSettings)
        {
            return;
        }

        if (IsNoStyleOverrideProfile(profile))
        {
            _credentialManager.SetCleanupStyleOverride(CleanupStyleOverrideKind.None, null);
        }
        else if (IsClassicStyleOverrideProfile(profile))
        {
            _credentialManager.SetCleanupStyleOverride(CleanupStyleOverrideKind.Classic, null);
        }
        else if (IsCustomPromptProfile(profile))
        {
            _credentialManager.SetCleanupStyleOverride(CleanupStyleOverrideKind.CustomPrompt, profile.Id);
        }
    }

    private static PromptProfile CreateNoStyleOverrideProfile() => new()
    {
        Id = 0,
        Name = "None",
        Content = string.Empty,
        IsBuiltIn = true,
        SortOrder = 0
    };

    private static PromptProfile CreateClassicStyleOverrideProfile() => new()
    {
        Id = -2,
        Name = PromptConfig.DefaultProfileName,
        Content = BuiltInPrompts.ClassicStyleOverrideGuidance,
        IsBuiltIn = true,
        SortOrder = 1
    };

    private static bool IsNoStyleOverrideProfile(PromptProfile profile) =>
        profile.Id == 0 && string.Equals(profile.Name, "None", StringComparison.Ordinal);

    private static bool IsClassicStyleOverrideProfile(PromptProfile profile) =>
        string.Equals(profile.Name, PromptConfig.DefaultProfileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsCustomPromptProfile(PromptProfile profile) =>
        !profile.IsBuiltIn && profile.Id > 0;



    private void ClearApiKey(string provider)
    {
        if (!ConfirmationDialog.Show("Confirm", $"Are you sure you want to clear the {provider} API key?",
            confirmText: "Clear", cancelText: "Cancel", centerOnContentArea: true)) return;

        if (!ProviderModelCatalog.TryParseProvider(provider, out var parsedProvider))
        {
            return;
        }

        switch (parsedProvider)
        {
            case TranscriptionProvider.Gemini:
                _credentialManager.DeleteApiKey();
                GeminiApiKey = string.Empty;
                break;
            case TranscriptionProvider.OpenRouter:
                _credentialManager.DeleteOpenRouterApiKey();
                OpenRouterApiKey = string.Empty;
                break;
            case TranscriptionProvider.Groq:
                _credentialManager.DeleteGroqApiKey();
                GroqApiKey = string.Empty;
                break;
        }

        NotificationDialog.ShowSuccess("Success", $"{provider} API key has been cleared", centerOnContentArea: true);
    }

    private void SaveApiKeys()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(GeminiApiKey) && !GeminiApiKey.Contains("*"))
                _credentialManager.SaveApiKey(GeminiApiKey);

            if (!string.IsNullOrWhiteSpace(OpenRouterApiKey) && !OpenRouterApiKey.Contains("*"))
                _credentialManager.SaveOpenRouterApiKey(OpenRouterApiKey);

            if (!string.IsNullOrWhiteSpace(GroqApiKey) && !GroqApiKey.Contains("*"))
                _credentialManager.SaveGroqApiKey(GroqApiKey);

            // Provider and model are auto-saved in property setters - no need to save here

            _credentialManager.SetTextFormattingMode(FormattingMode == "Markdown" ? Models.Configuration.TextFormattingMode.Markdown : Models.Configuration.TextFormattingMode.PlainText);

            // Map retention string back to days
            int retentionDays = StorageRetention switch
            {
                "7 Days" => 7,
                "30 Days" => 30,
                "90 Days" => 90,
                _ => 0
            };
            _appSettings.AudioRetentionDays = retentionDays;

            // Reactive cleanup: Trigger cleanup immediately when user saves new retention period
            System.Threading.Tasks.Task.Run(() => _appSettings.CleanupOldFilesNow());

            NotificationDialog.ShowSuccess("Success", "Settings saved successfully", centerOnContentArea: true);

            // Reload to show masked keys
            LoadSettings();
        }
        catch (Exception ex)
        {
            NotificationDialog.ShowError("Error", $"Failed to save settings: {ex.Message}", centerOnContentArea: true);
        }
    }

    private void LoadSettings()
    {
        _isLoadingSettings = true;
        // Capture connected state to preserve it across reloads (masking triggers reset)
        bool geminiWasConnected = GeminiStatus == ApiKeyStatus.Connected;
        bool openRouterWasConnected = OpenRouterStatus == ApiKeyStatus.Connected;
        bool groqWasConnected = GroqStatus == ApiKeyStatus.Connected;

        try
        {
            var activeProvider = _credentialManager.GetActiveProvider();
            string activeModelId = _credentialManager.GetModelId(activeProvider);

            bool oldSuppressProviderPersistence = _suppressProviderPersistence;
            _suppressProviderPersistence = true;
            try
            {
                SelectedProvider = ProviderModelCatalog.GetAsrDisplayName(activeProvider);
            }
            finally
            {
                _suppressProviderPersistence = oldSuppressProviderPersistence;
            }

            UpdateAvailableModels(activeModelId);

            var cleanupProvider = _credentialManager.GetActiveCleanupProvider();
            string cleanupModelId = _credentialManager.GetCleanupModelId(cleanupProvider);
            IsTranscriptCleanupEnabled = _credentialManager.IsTranscriptCleanupEnabled();
            SelectedCleanupProvider = ProviderModelCatalog.GetDisplayName(cleanupProvider);
            UpdateAvailableCleanupModels(cleanupModelId);
            FormattingMode = _credentialManager.GetTextFormattingMode() == TextFormattingMode.Markdown ? "Markdown" : "Plain Text";
            SelectedCleanupContextMode = _credentialManager.GetCleanupContextMode().ToString();
            SelectedCleanupIntensity = _credentialManager.GetCleanupIntensity().ToString();
        }
        finally
        {
            _isLoadingSettings = false;
        }

        GeminiApiKey = MaskKey(_credentialManager.GetUserApiKey());
        OpenRouterApiKey = MaskKey(_credentialManager.GetUserOpenRouterApiKey());
        GroqApiKey = MaskKey(_credentialManager.GetUserGroqApiKey());

        StorageRetention = _appSettings.AudioRetentionDays switch
        {
            7 => "7 Days",
            30 => "30 Days",
            90 => "90 Days",
            _ => "Never Delete"
        };

        LaunchOnStartup = _appSettings.LaunchOnStartup;
        MinimizeToTray = _appSettings.MinimizeToTray;
        MuteSystemAudioDuringRecording = _appSettings.MuteSystemAudioDuringRecording;
        EnableSmartInsertion = _appSettings.EnableSmartInsertion;
        EnableNativeTypingInjection = _appSettings.EnableNativeTypingInjection;

        CurrentHotkeyDisplay = FormatHotkeyText(_appSettings.GlobalHotkeyKey, _appSettings.GlobalHotkeyModifiers);

        // Restore status logic
        if (string.IsNullOrWhiteSpace(GeminiApiKey))
            GeminiStatus = ApiKeyStatus.NotConfigured;
        else if (geminiWasConnected)
            GeminiStatus = ApiKeyStatus.Connected;
        else
            GeminiStatus = ApiKeyStatus.Configured;

        if (string.IsNullOrWhiteSpace(OpenRouterApiKey))
            OpenRouterStatus = ApiKeyStatus.NotConfigured;
        else if (openRouterWasConnected)
            OpenRouterStatus = ApiKeyStatus.Connected;
        else
            OpenRouterStatus = ApiKeyStatus.Configured;

        if (string.IsNullOrWhiteSpace(GroqApiKey))
            GroqStatus = ApiKeyStatus.NotConfigured;
        else if (groqWasConnected)
            GroqStatus = ApiKeyStatus.Connected;
        else
            GroqStatus = ApiKeyStatus.Configured;

        SettingsLoaded?.Invoke(this, EventArgs.Empty);
    }

    private string MaskKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (key.Length <= 8) return "********";
        return key.Substring(0, 4) + "********" + key.Substring(key.Length - 4);
    }

    private async Task TestConnectionAsync(string? providerName = null)
    {
        try
        {
            providerName ??= SelectedProvider;
            if (!ProviderModelCatalog.TryParseProvider(providerName, out var provider))
            {
                NotificationDialog.ShowError("Error", "Unknown provider selected.", centerOnContentArea: true);
                return;
            }

            string? keyToTest = GetUnmaskedApiKeyInput(provider);
            ApiKeyValidationResult result = provider switch
            {
                TranscriptionProvider.Gemini => await _geminiApiClient.ValidateApiKeyAsync(keyToTest),
                TranscriptionProvider.OpenRouter => await _openRouterClient.ValidateApiKeyAsync(keyToTest),
                TranscriptionProvider.Groq => await _groqApiClient.ValidateApiKeyAsync(keyToTest),
                _ => throw new InvalidOperationException("Unknown provider selected.")
            };

            SetApiKeyStatus(provider, result.IsValid ? ApiKeyStatus.Connected : ApiKeyStatus.Error);
            string providerDisplayName = ProviderModelCatalog.GetDisplayName(provider);

            if (result.IsValid)
            {
                NotificationDialog.ShowSuccess("Test Connection", $"Connection to {providerDisplayName} verified successfully!\n\nStatus: Online\nAPI Key: Valid", centerOnContentArea: true);
            }
            else
            {
                NotificationDialog.ShowWarning("Test Connection Failed", $"Failed to connect to {providerDisplayName}.\n\nError: {result.ErrorMessage}", centerOnContentArea: true);
            }
        }
        catch (Exception ex)
        {
            NotificationDialog.ShowError("Error", $"An unexpected error occurred during connection test: {ex.Message}", centerOnContentArea: true);
        }
    }

    private string? GetUnmaskedApiKeyInput(TranscriptionProvider provider)
    {
        string key = provider switch
        {
            TranscriptionProvider.Gemini => GeminiApiKey,
            TranscriptionProvider.OpenRouter => OpenRouterApiKey,
            TranscriptionProvider.Groq => GroqApiKey,
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(key) && !key.Contains("*") ? key : null;
    }

    private void SetApiKeyStatus(TranscriptionProvider provider, ApiKeyStatus status)
    {
        switch (provider)
        {
            case TranscriptionProvider.Gemini:
                GeminiStatus = status;
                break;
            case TranscriptionProvider.OpenRouter:
                OpenRouterStatus = status;
                break;
            case TranscriptionProvider.Groq:
                GroqStatus = status;
                break;
        }
    }



    private void CreateProfile()
    {
        // This is now effectively "Save New Profile" called from the Save button when in "Create New" mode
        // Or "Duplicate" logic
        
        var newProfile = new PromptProfile
        {
            Name = PromptName,
            Content = SystemPrompt,
            IsBuiltIn = false
        };
        
        _promptDatabase.SavePrompt(newProfile);
        LoadPrompts();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == newProfile.Id);
    }
    
    private void SaveProfile()
    {
        if (SelectedProfile == null) return;

        // If we choose "Create New...", treating "SaveProfile" as "Create"
        if (SelectedProfile.Name == "Create New...")
        {
            CreateProfile();
            return;
        }

        // Standard update for custom profiles
        if (!SelectedProfile.IsBuiltIn)
        {
            SelectedProfile.Name = PromptName; // Using hidden name field value? No, name is hidden, so keep original name?
            // Actually user said: "only need the name field when we are creating a new prompt"
            // So for existing custom prompts, we should NOT update name from UI unless we made it visible.
            // But we made it hidden. So we should NOT update Name.
            // SelectedProfile.Name = PromptName; <--- remove this if hidden
            
            SelectedProfile.Content = SystemPrompt;
            _promptDatabase.SavePrompt(SelectedProfile);

            NotificationDialog.ShowSuccess("Success", "Profile updated successfully.", centerOnContentArea: true);
            return;
        }
        
        // If built-in, "Save" should not be reachable due to IsSaveVisible=false logic.
        // But if we called it via command, ignore.
    }
    
    // Using SaveProfileCommand for Duplicate/SaveAs logic? Or creating a new command?
    // Let's reuse CreateProfile for "Save As New" logic.
    // We need a specific "Duplicate" command action.
    public ICommand DuplicateProfileCommand => new RelayCommand(DuplicateProfile);

    private void DuplicateProfile()
    {
        if (SelectedProfile == null) return;

        // Enter "Create New" mode but pre-filled
        string copyContent = SelectedProfile.Content;
        
        // Find "Create New..." profile
        var createNewProfile = Profiles.FirstOrDefault(p => p.Name == "Create New...");
        if (createNewProfile != null)
        {
            SelectedProfile = createNewProfile;
            // Override defaults
            PromptName = $"Copy of {PromptName}"; // Pre-fill name
            SystemPrompt = copyContent;           // Pre-fill content
        }
    }

    private void DeleteProfile()
    {
        if (SelectedProfile == null || SelectedProfile.IsBuiltIn) return;

        if (ConfirmationDialog.ShowDestructive("Confirm Delete", $"Are you sure you want to delete profile '{SelectedProfile.Name}'?",
            confirmText: "Delete", cancelText: "Cancel", centerOnContentArea: true))
        {
            if (_credentialManager.GetCleanupStyleOverrideKind() == CleanupStyleOverrideKind.CustomPrompt &&
                _credentialManager.GetCleanupStylePromptId() == SelectedProfile.Id)
            {
                _credentialManager.SetCleanupStyleOverride(CleanupStyleOverrideKind.None, null);
            }

            _promptDatabase.DeletePrompt(SelectedProfile.Id);
            LoadPrompts();
        }
    }

    private void UpdateSystemPrompt()
    {
        // Deprecated/Handled in Setter properties
    }

    private void LoadSystemInfo()
    {
        var info = new System.Text.StringBuilder();

        // Technical Environment
        info.AppendLine("--- ENVIRONMENT ---");
        info.AppendLine($"OS: {System.Environment.OSVersion.Platform} {System.Environment.OSVersion.Version}");
        info.AppendLine($"Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        info.AppendLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        
        // Hardware Capacity
        info.AppendLine("");
        info.AppendLine("--- HARDWARE ---");
        info.AppendLine($"CPU Cores: {System.Environment.ProcessorCount}");
        var totalMemoryMB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
        info.AppendLine($"System RAM: ~{totalMemoryMB:N0} MB");
        
        // App Performance
        info.AppendLine("");
        info.AppendLine("--- APP STATE ---");
        var currentMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
        info.AppendLine($"Memory Usage: {currentMemoryMB:N0} MB");
        var uptime = System.TimeSpan.FromMilliseconds(System.Environment.TickCount64);
        info.AppendLine($"Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m");

        // Display
        var primaryScreen = System.Windows.SystemParameters.PrimaryScreenWidth;
        var primaryScreenHeight = System.Windows.SystemParameters.PrimaryScreenHeight;
        info.AppendLine($"Display: {primaryScreen}x{primaryScreenHeight}");

        SystemInfo = info.ToString().TrimEnd();
    }

    private void ExecuteRefreshLogs()
    {
        // Restart timer to reset the 3s interval
        _refreshTimer.Stop();
        
        // Perform instant refresh
        RefreshDiagnostics();
        
        _refreshTimer.Start();
    }

    private void RefreshDiagnostics()
    {
        try
        {
            // 1. Load Fresh Performance Data from History DB
            if (System.Windows.Application.Current is App app && app.Services?.HistoryDatabase != null)
            {
                // Task.Run to keep DB query off the UI thread
                System.Threading.Tasks.Task.Run(async () => 
                {
                    var history = await app.Services.HistoryDatabase.GetAllEntriesAsync();
                    var lastTen = history.Take(10).ToList();
                    
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Update LatencyData if it has changed
                        var currentLatencies = lastTen.Where(e => e.LatencySeconds > 0).Select(e => e.LatencySeconds).ToList();
                        
                        // Simple check to avoid unnecessary collection resets if nothing changed
                        if (LatencyData.Count != currentLatencies.Count || 
                            !LatencyData.SequenceEqual(currentLatencies))
                        {
                            LatencyData.Clear();
                            foreach (var latency in currentLatencies)
                            {
                                LatencyData.Add(latency);
                            }

                            if (currentLatencies.Count >= 3)
                            {
                                var avg = currentLatencies.Average();
                                AverageLatency = $"{avg:F2}s average";
                            }
                            else if (currentLatencies.Count > 0)
                            {
                                AverageLatency = "Collecting more data...";
                            }
                            else
                            {
                                AverageLatency = "No data yet";
                            }
                        }
                    });
                });
            }

            // 2. Thread-safe update of application logs (newest first)
            var recentLogs = Logger.Instance.GetRecentLogs(20).OrderByDescending(e => e.Timestamp).ToList();
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                LogEntries.Clear();
                foreach (var entry in recentLogs)
                {
                    LogEntries.Add(new LogEntry
                    {
                        Timestamp = entry.Timestamp.ToString("HH:mm:ss"),
                        Level = entry.Level.ToString(),
                        Message = entry.Message
                    });
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] Diagnostics refresh failed: {ex.Message}");
        }
    }

    public void RefreshDiagnosticsNow()
    {
        RefreshDiagnostics();
    }

    private void CopyLogs()
    {
        var logsText = string.Join("\n", LogEntries.Select(l => $"[{l.Timestamp}] {l.Level}: {l.Message}"));
        System.Windows.Clipboard.SetText(logsText);
        NotificationDialog.ShowSuccess("Success", "Logs copied to clipboard", centerOnContentArea: true);
    }

    private void ClearLogs()
    {
        if (ConfirmationDialog.ShowDestructive("Confirm", "Are you sure you want to clear all log entries from the disk?",
            confirmText: "Clear Logs", cancelText: "Cancel", centerOnContentArea: true))
        {
            // Physically clear the log file and queue
            Logger.Instance.ClearLogFile();

            // Clear the UI collection
            System.Windows.Application.Current.Dispatcher.Invoke(() => LogEntries.Clear());

            NotificationDialog.ShowSuccess("Success", "All log entries have been cleared from disk", centerOnContentArea: true);
        }
    }

    private void ExecuteCopySystemInfo()
    {
        try
        {
            System.Windows.Clipboard.SetText(SystemInfo);
            ToastService.Instance.Success("System info copied to clipboard");
        }
        catch
        {
            ToastService.Instance.Error("Failed to copy system info");
        }
    }

    #region Hotkey Methods

    // VK code constants for modifier identification
    private const int VK_SHIFT    = 0x10;
    private const int VK_CONTROL  = 0x11;
    private const int VK_MENU     = 0x12;  // Alt
    private const int VK_LSHIFT   = 0xA0;
    private const int VK_RSHIFT   = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU    = 0xA4;
    private const int VK_RMENU    = 0xA5;
    private const int VK_LWIN     = 0x5B;
    private const int VK_RWIN     = 0x5C;

    private static bool IsModifierVk(int vkCode)
    {
        return vkCode == VK_SHIFT || vkCode == VK_CONTROL || vkCode == VK_MENU ||
               vkCode == VK_LSHIFT || vkCode == VK_RSHIFT ||
               vkCode == VK_LCONTROL || vkCode == VK_RCONTROL ||
               vkCode == VK_LMENU || vkCode == VK_RMENU ||
               vkCode == VK_LWIN || vkCode == VK_RWIN;
    }

    private static ModifierKeys VkToModifier(int vkCode)
    {
        return vkCode switch
        {
            VK_SHIFT or VK_LSHIFT or VK_RSHIFT       => ModifierKeys.Shift,
            VK_CONTROL or VK_LCONTROL or VK_RCONTROL  => ModifierKeys.Control,
            VK_MENU or VK_LMENU or VK_RMENU           => ModifierKeys.Alt,
            VK_LWIN or VK_RWIN                         => ModifierKeys.Windows,
            _ => ModifierKeys.None,
        };
    }

    private void StartHotkeyEdit()
    {
        IsHotkeyEditMode = true;
        PendingHotkeyDisplay = "Press Keys...";
        _pendingKey = Keys.None;
        _pendingModifiers = ModifierKeys.None;

        // Start a low-level keyboard hook so we can capture Win/Alt keys
        // that the OS shell would otherwise swallow before WPF sees them
        _keyboardHook?.Dispose();
        _keyboardHook = new GlobalKeyboardHook();
        _keyboardHook.KeyDown += OnHookKeyDown;
        _keyboardHook.KeyUp += OnHookKeyUp;
        _keyboardHook.Start();
    }

    private void StopKeyboardHook()
    {
        if (_keyboardHook != null)
        {
            _keyboardHook.KeyDown -= OnHookKeyDown;
            _keyboardHook.KeyUp -= OnHookKeyUp;
            _keyboardHook.Dispose();
            _keyboardHook = null;
        }
    }

    private void OnHookKeyDown(object? sender, int vkCode)
    {
        if (!IsHotkeyEditMode) return;

        if (IsModifierVk(vkCode))
        {
            // Accumulate modifier — show "Ctrl + Win + ..." while held
            _pendingModifiers |= VkToModifier(vkCode);
            PendingHotkeyDisplay = FormatHotkeyText(null, _pendingModifiers);
        }
        else
        {
            // Non-modifier key finalizes the combo
            _pendingKey = (Keys)vkCode;
            PendingHotkeyDisplay = FormatHotkeyText(_pendingKey, _pendingModifiers);
        }
    }

    private void OnHookKeyUp(object? sender, int vkCode)
    {
        // Intentionally a no-op. Once a key or modifier is captured,
        // it stays in the display until the user clicks Save, Clear, or Cancel.
    }

    private void SaveHotkey()
    {
        // Save if we have a non-modifier key, or at least one modifier
        if (_pendingKey != Keys.None || _pendingModifiers != ModifierKeys.None)
        {
            // Try to register before persisting — if it fails (e.g., taken by
            // another app), stay in edit mode so the user can pick something else.
            bool registered = _hotkeyManager.RegisterHotkey(_pendingKey, _pendingModifiers);

            if (!registered)
            {
                // Keep the hook alive so the user can Clear and try a different combo
                System.Diagnostics.Debug.WriteLine("[Settings] Hotkey registration failed. Staying in edit mode.");
                return;
            }

            // Registration succeeded — stop the hook, persist, and exit edit mode
            StopKeyboardHook();
            _appSettings.GlobalHotkeyKey = _pendingKey;
            _appSettings.GlobalHotkeyModifiers = _pendingModifiers;
            CurrentHotkeyDisplay = FormatHotkeyText(_pendingKey, _pendingModifiers);

            System.Diagnostics.Debug.WriteLine($"[Settings] Saved new hotkey: {CurrentHotkeyDisplay}");
            IsHotkeyEditMode = false;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[Settings] Save attempted with nothing captured.");
            NotificationDialog.ShowWarning("No Hotkey", "No keys were captured. Press a key combination first.", centerOnContentArea: true);
        }
    }

    private void CancelHotkeyEdit()
    {
        StopKeyboardHook();
        IsHotkeyEditMode = false;
        // Reset pending state
        _pendingKey = Keys.None;
        _pendingModifiers = ModifierKeys.None;
    }

    private void ClearHotkey()
    {
        // Just reset the pending state and text so user can start fresh
        // but STAY in edit mode and don't unregister anything yet
        _pendingKey = Keys.None;
        _pendingModifiers = ModifierKeys.None;
        PendingHotkeyDisplay = "Press Keys...";

        System.Diagnostics.Debug.WriteLine("[Settings] Hotkey input cleared. Listener remains active.");
    }

    private string FormatHotkeyText(Keys? key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        if (key.HasValue && key.Value != Keys.None)
        {
            string keyName = key.Value.ToString();
            if (key.Value == Keys.Space) keyName = "Space";
            parts.Add(keyName);
        }
        else if (parts.Count == 0)
        {
            return "Press Keys...";
        }

        return string.Join(" + ", parts);
    }

    #endregion

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _offlineEngineInstallCancellation?.Cancel();
        if (_refreshTimer != null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTimerTick;
        }

        if (_offlineEngineProgressTimer != null)
        {
            _offlineEngineProgressTimer.Stop();
            _offlineEngineProgressTimer.Tick -= OnOfflineEngineProgressTimerTick;
        }

        _hotkeyManager.HotkeyChanged -= OnHotkeyChanged;
        _hotkeyManager.HotkeyRegistrationFailed -= OnHotkeyRegistrationFailed;
        StopKeyboardHook();
    }
}

public class LogEntry
{
    public string Timestamp { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
