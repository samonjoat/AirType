using System;
using System.Windows;
using AirType.Services;
using AirType.Services.Database;
using AirType.Models;
using AirType.Models.Configuration;
using AirType.Views;
using AirType.ViewModels;
using AirType.Services.Transcription;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using System.Collections.Generic;
using AirType.Services.Audio;

namespace AirType
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private ServiceContainer? _services;
        private TrayManager? _trayManager;
        private CapsuleWidget? _capsuleWidget;
        private AppWorkflowUICallbacks? _workflowCallbacks;
        private AppWorkflowUICallbacks? _rerunWorkflowCallbacks;
        private MainWindow? _mainWindow;
        private SingleInstanceCoordinator? _singleInstanceCoordinator;
        private GlobalKeyboardHook? _runtimeCancelHook;
        private ILocalAsrSession? _activeLocalAsrSession;
        private readonly object _localAsrFrameLock = new();
        private readonly Queue<AirType.Models.Transcription.AudioPcmFrame> _pendingLocalAsrFrames = new();
        private Guid? _pendingLocalAsrRecordingId;
        private bool _isExplicitShutdownRequested;
        private bool _isEscapeKeyDown;
        private bool _isEscapeCancellationInProgress;
        private const int EscapeVirtualKey = 0x1B;

        static App()
        {
            WindowsEnvironmentGuard.EnsureWindir();
        }

        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            string msg = e.ExceptionObject is Exception ex ? ex.ToString() : "Unknown Error";
            Console.WriteLine("CRITICAL (AppDomain): " + msg);
            // System.Windows.MessageBox.Show("Critical AppDomain Error: " + msg);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Console.WriteLine("CRITICAL (Dispatcher): " + e.Exception.ToString());
            // System.Windows.MessageBox.Show("Critical Dispatcher Error: " + e.Exception.Message);
            // e.Handled = true; 
        }

        // Window context tracking
        private WindowContext? _capturedWindowContext;
        private WindowContext? _lastWindowContext;
        private static readonly string CurrentProcessName = Process.GetCurrentProcess().ProcessName;

        public ServiceContainer? Services => _services;
        public AppWorkflowUICallbacks? WorkflowCallbacks => _workflowCallbacks;
        public AppWorkflowUICallbacks? RerunWorkflowCallbacks => _rerunWorkflowCallbacks;
        internal bool IsExplicitShutdownRequested => _isExplicitShutdownRequested;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                _singleInstanceCoordinator = new SingleInstanceCoordinator();
                _singleInstanceCoordinator.ActivationRequested += OnSingleInstanceActivationRequested;

                if (!_singleInstanceCoordinator.TryAcquirePrimaryInstance())
                {
                    Debug.WriteLine("[App] secondary instance signaled primary");

                    if (!await _singleInstanceCoordinator.SignalPrimaryInstanceAsync())
                    {
                        Debug.WriteLine("[App] secondary instance failed to signal primary");
                    }

                    RequestExplicitShutdown();
                    return;
                }

                TerminateDuplicateAirTypeProcesses();

                var migrationResults = DataFolderMigrator.Run();
                var migrationFailure = migrationResults.FirstOrDefault(r => r.IsFailure);
                if (migrationFailure.IsFailure)
                {
                    MessageBox.Show(
                        $"AirType could not prepare its user data folder.\n\n{migrationFailure.Detail}",
                        "AirType — Data migration failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    RequestExplicitShutdown(1);
                    return;
                }

                Logger.Info("App", "primary instance acquired");

                // 1. Check if database is missing but backup exists - offer to restore
                if (!DatabaseInitializer.DatabaseExists && DatabaseInitializer.BackupExists)
                {
                    // Temporarily prevent app from auto-closing when dialog closes
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    
                    var backupTime = DatabaseInitializer.GetLatestBackupTime();
                    var choice = Views.RestoreBackupDialog.Show(backupTime);
                    if (choice == Views.RestoreChoice.Restore)
                    {
                        DatabaseInitializer.RestoreFromBackup();
                        Logger.Info("App", "Database restored from backup");
                    }
                    else if (choice == Views.RestoreChoice.Exit)
                    {
                        RequestExplicitShutdown();
                        return;
                    }
                    // If StartFresh, Initialize() will create a new database
                    
                    // Restore normal shutdown mode
                    ShutdownMode = ShutdownMode.OnMainWindowClose;
                }

                // 2. Initialize Database (run migrations)
                DatabaseInitializer.Initialize();

                // 3. Initialize Backend Services
                _services = new ServiceContainer();

                // 3a. Warm up API connections in background (DNS + TLS pre-connect)
                _ = _services.WarmupConnectionsAsync();

                // 4. Wire up AudioInputManager events
                if (_services.AudioInputManager is AudioInputManager audioManager)
                {
                    audioManager.RecordingStateChanged += OnRecordingStateChanged;
                    audioManager.PcmFrameAvailable += OnPcmFrameAvailable;
                }

                // 5. Initialize Workflow Callbacks
                _workflowCallbacks = CreateWorkflowCallbacks();
                _rerunWorkflowCallbacks = CreateRerunWorkflowCallbacks();

                // 6. Show Main Window as soon as the UI has the services it needs.
                ShowMainWindow();

                // 7. Initialize System Tray
                _trayManager = new TrayManager();
                _trayManager.OpenFullUIRequested += (s, ev) => ShowMainWindow();
                _trayManager.SettingsRequested += (s, ev) => ShowSettings();
                _trayManager.HistoryRequested += (s, ev) => ShowHistory();
                _trayManager.NotesRequested += (s, ev) => ShowNotes();
                _trayManager.DictionaryRequested += (s, ev) => ShowDictionary();
                _trayManager.ExitRequested += (s, ev) => RequestExplicitShutdown();
                _trayManager.Initialize();

                // 8. Initialize Capsule Widget
                InitializeCapsuleWidgetAsync();

                // 8a. Initialize global Escape cancellation
                InitializeRuntimeCancelHook();

                _singleInstanceCoordinator.MarkStartupComplete();
            }
            catch (Exception ex)
            {
                NotificationDialog.ShowError("Critical Error", $"Application failed to start: {ex.Message}");
                RequestExplicitShutdown();
            }
        }

        private void RequestExplicitShutdown(int exitCode = 0)
        {
            _isExplicitShutdownRequested = true;

            if (exitCode == 0)
            {
                Shutdown();
            }
            else
            {
                Shutdown(exitCode);
            }
        }

        private static void TerminateDuplicateAirTypeProcesses()
        {
            using var currentProcess = Process.GetCurrentProcess();
            string? currentPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                return;
            }

            foreach (var process in Process.GetProcessesByName(CurrentProcessName))
            {
                try
                {
                    if (process.Id == currentProcess.Id)
                    {
                        continue;
                    }

                    string? processPath = process.MainModule?.FileName;
                    if (!string.Equals(processPath, currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Logger.Warn("App", $"Terminating duplicate AirType process {process.Id} from the same executable path.");
                    if (!process.CloseMainWindow() || !process.WaitForExit(1500))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("App", $"Failed to inspect or terminate duplicate AirType process {process.Id}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private void RegisterGlobalHotkey()
        {
            if (_services?.HotkeyManager != null && _services?.AppSettingsManager != null)
            {
                var key = _services.AppSettingsManager.GlobalHotkeyKey;
                var modifiers = _services.AppSettingsManager.GlobalHotkeyModifiers;
                
                System.Diagnostics.Debug.WriteLine($"[Application] Registering global hotkey: {modifiers} + {key}");
                
                // Try to register the configured hotkey with a fallback to Ctrl+F2 if it fails
                bool success = _services.HotkeyManager.RegisterHotkey(key, modifiers);
                if (!success)
                {
                    _services.HotkeyManager.RegisterHotkeyWithFallback(
                        key, modifiers,
                        (System.Windows.Forms.Keys.F2, System.Windows.Input.ModifierKeys.Control),
                        (System.Windows.Forms.Keys.F3, System.Windows.Input.ModifierKeys.Control)
                    );
                }
            }
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
            }

            _mainWindow.RestoreForActivation();
        }

        private void OnSingleInstanceActivationRequested()
        {
            Logger.Info("App", "primary instance activation requested");
            Dispatcher.BeginInvoke(new Action(ShowMainWindow));
        }

        private void InitializeCapsuleWidgetAsync()
        {
            try
            {
                _capsuleWidget = new CapsuleWidget();
                
                // Connect AudioInputManager for waveform visualization and recording
                if (_services?.AudioInputManager is AudioInputManager audioManager)
                {
                    _capsuleWidget.SetAudioInputManager(audioManager);
                }
                
                // Set hotkey manager
                if (_services?.HotkeyManager != null)
                {
                    _capsuleWidget.SetHotkeyManager(_services.HotkeyManager);
                }

                if (_capsuleWidget.DataContext is CapsuleWidgetViewModel capsuleVm)
                {
                    capsuleVm.StateManager.StateChanged += OnCapsuleStateChanged;
                }

                _capsuleWidget.SetCancelHandler(() => HandleUserCancellationAsync("capsule-button"));
                _capsuleWidget.Loaded += (s, e) => RegisterGlobalHotkey();
                _capsuleWidget.Show();
                
                // Position logic is handled inside CapsuleWidget.Loaded usually, 
                // but let's ensure it's DPI aware if possible.
                // In legacy it called EnsureDpiAwareStartupPositionAsync
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Application] CapsuleWidget initialization failed: {ex.Message}");
            }
        }

        private AppWorkflowUICallbacks CreateWorkflowCallbacks() =>
            BuildWorkflowCallbacks(UpdateWidgetWorkflowStage);

        private AppWorkflowUICallbacks CreateRerunWorkflowCallbacks() =>
            BuildWorkflowCallbacks(_ => { /* rerun is decoupled from the capsule widget */ });

        private AppWorkflowUICallbacks BuildWorkflowCallbacks(Action<TranscriptionWorkflowStage> widgetCallback)
        {
            return new AppWorkflowUICallbacks(
                updateWidgetWorkflowStage: widgetCallback,
                showWorkflowError: (title, message) =>
                {
                    Dispatcher.BeginInvoke(() => ShowRecordingFeedback(title, message, isError: true));
                },
                showBalloonTip: (title, message, isWarning) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (isWarning)
                        {
                            ToastService.Instance.Warning(message, title);
                        }
                        else
                        {
                            ToastService.Instance.Info(message, title);
                        }
                    });
                    return true;
                },
                promptUserWithSettingsOption: (title, message) =>
                {
                    return Dispatcher.Invoke(() =>
                    {
                        if (ConfirmationDialog.ShowWarning(title, message,
                            confirmText: "Open Settings", cancelText: "Cancel"))
                        {
                            ShowSettings();
                            return true;
                        }
                        return false;
                    });
                },
                refreshHistoryView: () => Dispatcher.BeginInvoke(new Action(() => _mainWindow?.RefreshVisibleHistory())),
                refreshPerformanceDiagnostics: () => Dispatcher.BeginInvoke(new Action(() => _mainWindow?.RefreshVisibleSettingsDiagnostics()))
            );
        }

        private void UpdateWidgetWorkflowStage(TranscriptionWorkflowStage stage)
        {
            Dispatcher.Invoke(() =>
            {
                if (_capsuleWidget?.DataContext is CapsuleWidgetViewModel vm)
                {
                    switch (stage)
                    {
                        case TranscriptionWorkflowStage.LoadingAudio:
                        case TranscriptionWorkflowStage.PreparingPayload:
                        case TranscriptionWorkflowStage.Transcribing:
                            vm.StateManager.TransitionToTranscribing();
                            break;

                        case TranscriptionWorkflowStage.Cleaning:
                            vm.StateManager.TransitionToCleaning();
                            break;

                        case TranscriptionWorkflowStage.Failed:
                            vm.StateManager.TransitionToFailureFlash();
                            break;

                        case TranscriptionWorkflowStage.Idle:
                        case TranscriptionWorkflowStage.Persisting:
                        case TranscriptionWorkflowStage.Injecting:
                        case TranscriptionWorkflowStage.Completed:
                        case TranscriptionWorkflowStage.Cancelled:
                            vm.StateManager.TransitionFromWorkflow();
                            break;
                    }
                }
            });
        }

        private void OnCapsuleStateChanged(object? sender, WidgetStateChangedEventArgs e)
        {
            Logger.Info("App", $"Capsule state changed: {e.PreviousState} -> {e.NewState}");

            if (_services?.AppSoundPolicy is not IAppSoundPolicy)
                return;

            var request = CreateCapsuleStateSoundRequest(
                e,
                _services?.TranscriptionWorkflowService?.IsWorkflowActive == true,
                _services?.AppSettingsManager?.MuteSystemAudioDuringRecording == true,
                TryMuteSystemAudioForActiveRecording);

            if (!request.HasValue)
            {
                if (e.NewState == WidgetStateManager.WidgetState.Recording && _services?.TranscriptionWorkflowService?.IsWorkflowActive == true)
                {
                    Logger.Info("App", "Suppressing start sound because workflow is already active");
                }

                return;
            }

            PlayAppSound(request.Value);
        }

        private void InitializeRuntimeCancelHook()
        {
            _runtimeCancelHook?.Dispose();
            _runtimeCancelHook = new GlobalKeyboardHook();
            _runtimeCancelHook.KeyDown += OnRuntimeCancelKeyDown;
            _runtimeCancelHook.KeyUp += OnRuntimeCancelKeyUp;
            _runtimeCancelHook.Start();
        }

        private void OnRuntimeCancelKeyUp(object? sender, int vkCode)
        {
            if (vkCode == EscapeVirtualKey)
            {
                _isEscapeKeyDown = false;
            }
        }

        private async void OnRuntimeCancelKeyDown(object? sender, int vkCode)
        {
            if (vkCode != EscapeVirtualKey)
                return;

            if (_isEscapeKeyDown)
                return;

            _isEscapeKeyDown = true;
            await HandleUserCancellationAsync("escape");
        }

        private async Task HandleUserCancellationAsync(string origin)
        {
            if (_isEscapeCancellationInProgress)
            {
                Logger.Info("App", $"{origin} cancel requested but cancellation is already in progress");
                return;
            }

            if (!CanCancelActiveOperation())
            {
                Logger.Info("App", $"{origin} cancel requested but no active recording or workflow is available to cancel");
                return;
            }

            _isEscapeCancellationInProgress = true;

            try
            {
                if (_services?.AudioInputManager is AudioInputManager audioManager && audioManager.IsRecording)
                {
                    Logger.Info("App", $"{origin} cancel routed to active recording");
                    await audioManager.CancelRecordingAsync();
                    await CancelActiveLocalAsrSessionAsync();
                    Logger.Info("App", $"{origin} recording cancellation completed");
                }
                else if (_services?.TranscriptionWorkflowService is { IsWorkflowActive: true } workflowService)
                {
                    Logger.Info("App", $"{origin} cancel routed to active workflow");
                    workflowService.CancelCurrentOperation();
                    Logger.Info("App", $"{origin} workflow cancellation requested");
                }
                else if (CapsuleStateManager?.IsRecording == true)
                {
                    Logger.Warn("App", $"{origin} cancel found recording widget state without an active audio session; resetting widget state");
                    CapsuleStateManager.CancelRecording();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("App", $"{origin} cancellation failed: {ex.Message}");
            }
            finally
            {
                _isEscapeCancellationInProgress = false;
            }
        }

        private bool CanCancelActiveOperation()
        {
            if (_services?.AudioInputManager is AudioInputManager audioManager && audioManager.IsRecording)
                return true;

            if (CapsuleStateManager?.IsRecording == true)
                return true;

            return _services?.TranscriptionWorkflowService?.IsWorkflowActive == true;
        }

        private void ShowRecordingFeedback(string title, string message, bool isError = false)
        {
            Logger.Info("App", $"Recording feedback ({(isError ? "error" : "info")}): {title}");

            if (isError)
            {
                ToastService.Instance.Error(message, title);
            }
            else
            {
                ToastService.Instance.Info(message, title);
            }
        }

        private async void OnRecordingStateChanged(object? sender, RecordingStateEventArgs e)
        {
            // Ignore recording events from Notes page - it handles its own workflow
            if (e.Source == RecordingSource.Notes)
                return;

            Logger.Info("App", $"Recording state changed: IsRecording={e.IsRecording}, Mode={e.Mode}, Source={e.Source}");

            if (e.IsRecording)
            {
                await HandleRecordingStartedAsync(e.Mode);
            }
            else
            {
                _services?.SystemAudioMuteService?.RestoreAfterRecording();
                await HandleRecordingStoppedAsync();
            }
        }

        private void TryMuteSystemAudioForActiveRecording()
        {
            try
            {
                var services = _services;
                if (services?.AppSettingsManager?.MuteSystemAudioDuringRecording != true)
                    return;

                if (services.AudioInputManager.IsRecording != true)
                {
                    Logger.Info("App", "Skipping delayed system-audio mute because recording is no longer active.");
                    return;
                }

                services.SystemAudioMuteService.TryMuteForRecording();
            }
            catch (Exception ex)
            {
                Logger.Warn("App", $"Failed to mute system audio after start cue: {ex.Message}");
            }
        }

        private async Task HandleRecordingStartedAsync(RecordingMode mode)
        {
            if (_services?.TranscriptionWorkflowService?.IsWorkflowActive == true)
            {
                UpdateWidgetWorkflowStage(TranscriptionWorkflowStage.Idle);
                ShowRecordingFeedback("AirType Busy", "Transcription is already in progress.", isError: true);

                if (_services.AudioInputManager is AudioInputManager audioManager && audioManager.IsRecording)
                {
                    await audioManager.CancelRecordingAsync();
                }
                return;
            }

            // Capture window context
            var windowManager = _services?.WindowManager;
            if (windowManager != null)
            {
                var context = windowManager.CaptureCurrentWindow();
                if (context != null && context.IsValid() && !IsApplicationWindow(context))
                {
                    _capturedWindowContext = context;
                    _lastWindowContext = context;
                }
                else
                {
                    _capturedWindowContext = _lastWindowContext;
                }
            }

            TranscriptionProvider? activeProvider = _services == null
                ? (TranscriptionProvider?)null
                : LocalAsrProviderAvailability.ResolveActiveProvider(
                    _services.CredentialManager,
                    _services.OfflineEngineManager,
                    persistFallbackToCloud: true);
            if (activeProvider == TranscriptionProvider.Local && _services?.AudioInputManager.CurrentSession is { } session)
            {
                BeginPendingLocalAsrBuffer(session.Id);
                try
                {
                    _activeLocalAsrSession = await _services.LocalAsrWorkerSupervisor.TryStartSessionAsync(session.Id, CancellationToken.None);
                    if (_activeLocalAsrSession == null)
                    {
                        Logger.Warn("App", "Local ASR session was not started; workflow will use cloud fallback if configured.");
                        ClearPendingLocalAsrFrames();
                    }
                    else
                    {
                        await FlushPendingLocalAsrFramesAsync(_activeLocalAsrSession);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("App", $"Failed to start local ASR session: {ex.Message}");
                    _activeLocalAsrSession = null;
                    ClearPendingLocalAsrFrames();
                }
            }
        }

        private async Task HandleRecordingStoppedAsync()
        {
            var audioManager = _services?.AudioInputManager as AudioInputManager;
            if (audioManager == null) return;

            var services = _services;
            if (services == null) return;

            var session = audioManager.CurrentSession;
            if (session == null)
            {
                Logger.Info("App", "Recording stopped with no current session; treating as cancelled or already cleaned up");
                await CancelActiveLocalAsrSessionAsync();
                audioManager.ClearAudioBuffer();
                return;
            }

            if (session.Status != RecordingStatus.Completed)
            {
                Logger.Info("App", $"Recording stopped without a completed session. Status={session.Status}");
                await CancelActiveLocalAsrSessionAsync();
                audioManager.ClearAudioBuffer();
                return;
            }

            if (string.IsNullOrEmpty(session.FilePath) || !File.Exists(session.FilePath))
            {
                Logger.Warn("App", $"Completed recording session is missing its audio file: {session.FilePath}");
                ShowRecordingFeedback("Recording Error", "Recording finished, but the audio file is unavailable.", isError: true);
                PlayAppSound(AppSoundRequest.Create(AppSoundState.WorkflowFailed));
                await CancelActiveLocalAsrSessionAsync();
                audioManager.ClearAudioBuffer();
                return;
            }

            var workflowService = services.TranscriptionWorkflowService;
            if (workflowService != null && _workflowCallbacks != null)
            {
                try
                {
                    // Suppress hotkey during workflow to prevent re-triggering
                    if (services.HotkeyManager != null)
                        services.HotkeyManager.IsSuppressed = true;

                    // Get active provider from settings
                    var activeProvider = LocalAsrProviderAvailability.ResolveActiveProvider(
                        services.CredentialManager,
                        services.OfflineEngineManager,
                        persistFallbackToCloud: true);
                    byte[]? preloadedAudioData = audioManager.GetBufferedAudioData();

                    var result = await workflowService.RunWorkflowAsync(
                        session,
                        activeProvider,
                        _capturedWindowContext,
                        _lastWindowContext,
                        forceClipboardCopy: false,
                        isTestRun: false,
                        _workflowCallbacks,
                        preloadedAudioData,
                        _activeLocalAsrSession);

                    PlayAppSound(CreateWorkflowResultSoundRequest(result));

                    _capturedWindowContext = null;

                    if (_activeLocalAsrSession != null)
                    {
                        await _activeLocalAsrSession.DisposeAsync();
                        _activeLocalAsrSession = null;
                    }
                }
                finally
                {
                    _capturedWindowContext = null;

                    // Re-enable hotkey after workflow completes
                    if (services.HotkeyManager != null)
                        services.HotkeyManager.IsSuppressed = false;

                    ClearPendingLocalAsrFrames();
                    audioManager.ClearAudioBuffer();
                }
            }
        }

        private void OnPcmFrameAvailable(object? sender, AudioPcmFrameEventArgs e)
        {
            var session = _activeLocalAsrSession;
            if (session == null || session.RecordingId != e.Frame.RecordingId)
            {
                BufferPendingLocalAsrFrame(e.Frame);
                return;
            }

            _ = SendLocalAsrFrameAsync(session, e.Frame);
        }

        private void BeginPendingLocalAsrBuffer(Guid recordingId)
        {
            lock (_localAsrFrameLock)
            {
                _pendingLocalAsrFrames.Clear();
                _pendingLocalAsrRecordingId = recordingId;
            }
        }

        private void BufferPendingLocalAsrFrame(AirType.Models.Transcription.AudioPcmFrame frame)
        {
            lock (_localAsrFrameLock)
            {
                if (_pendingLocalAsrRecordingId == null &&
                    _services != null &&
                    LocalAsrProviderAvailability.ResolveActiveProvider(
                        _services.CredentialManager,
                        _services.OfflineEngineManager) == TranscriptionProvider.Local)
                {
                    _pendingLocalAsrRecordingId = frame.RecordingId;
                }

                if (_pendingLocalAsrRecordingId != frame.RecordingId)
                {
                    return;
                }

                _pendingLocalAsrFrames.Enqueue(frame);
            }
        }

        private async Task FlushPendingLocalAsrFramesAsync(ILocalAsrSession session)
        {
            AirType.Models.Transcription.AudioPcmFrame[] frames;
            lock (_localAsrFrameLock)
            {
                if (_pendingLocalAsrRecordingId != session.RecordingId)
                {
                    return;
                }

                frames = _pendingLocalAsrFrames.ToArray();
                _pendingLocalAsrFrames.Clear();
                _pendingLocalAsrRecordingId = null;
            }

            foreach (var frame in frames.OrderBy(frame => frame.Sequence))
            {
                await SendLocalAsrFrameAsync(session, frame);
            }
        }

        private void ClearPendingLocalAsrFrames()
        {
            lock (_localAsrFrameLock)
            {
                _pendingLocalAsrFrames.Clear();
                _pendingLocalAsrRecordingId = null;
            }
        }

        private static async Task SendLocalAsrFrameAsync(ILocalAsrSession session, AirType.Models.Transcription.AudioPcmFrame frame)
        {
            try
            {
                await session.EnqueueFrameAsync(frame, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Warn("App", $"Failed to stream local ASR frame: {ex.Message}");
            }
        }

        private async Task CancelActiveLocalAsrSessionAsync()
        {
            var session = _activeLocalAsrSession;
            _activeLocalAsrSession = null;
            ClearPendingLocalAsrFrames();
            if (session == null)
            {
                return;
            }

            try
            {
                await session.CancelAsync(CancellationToken.None);
                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn("App", $"Failed to cancel local ASR session: {ex.Message}");
            }
        }

        private void PlayAppSound(AppSoundRequest request)
        {
            var policy = _services?.AppSoundPolicy;
            if (policy == null)
                return;

            Logger.Info("App", $"Sound state: {request.State} -> {MapSoundCue(request)}");
            policy.Play(request);
        }

        private static AppSoundRequest? CreateCapsuleStateSoundRequest(
            WidgetStateChangedEventArgs e,
            bool isWorkflowActive,
            bool muteSystemAudioDuringRecording,
            Action onStartPlaybackCompleted)
        {
            ArgumentNullException.ThrowIfNull(e);
            ArgumentNullException.ThrowIfNull(onStartPlaybackCompleted);

            if (e.NewState == WidgetStateManager.WidgetState.Recording)
            {
                if (isWorkflowActive)
                {
                    return null;
                }

                return muteSystemAudioDuringRecording
                    ? AppSoundRequest.CreateRecordingStarted(onStartPlaybackCompleted)
                    : AppSoundRequest.Create(AppSoundState.RecordingStarted);
            }

            if ((e.NewState == WidgetStateManager.WidgetState.IdleMinimal || e.NewState == WidgetStateManager.WidgetState.IdleHover)
                && e.PreviousState == WidgetStateManager.WidgetState.Recording)
            {
                return AppSoundRequest.Create(AppSoundState.RecordingCanceledByUser);
            }

            return null;
        }

        private static AppSoundRequest CreateWorkflowResultSoundRequest(TranscriptionWorkflowResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (result.WasCancelled)
            {
                return AppSoundRequest.Create(AppSoundState.WorkflowCanceled);
            }

            if (result.InjectionSucceeded && !result.ClipboardFallbackUsed)
            {
                return AppSoundRequest.CreateInjectionSucceeded(injectionVerified: true, clipboardFallbackUsed: false);
            }

            if (result.Success)
            {
                return AppSoundRequest.Create(AppSoundState.Silent);
            }

            return AppSoundRequest.Create(AppSoundState.WorkflowFailed);
        }

        private static string MapSoundCue(AppSoundRequest request)
        {
            return request.State switch
            {
                AppSoundState.RecordingStarted => "Start",
                AppSoundState.RecordingCanceledByUser => "Cancel",
                AppSoundState.WorkflowCanceled => "Cancel",
                AppSoundState.WorkflowFailed => "Cancel",
                AppSoundState.InjectionSucceeded when request.InjectionVerified && !request.ClipboardFallbackUsed => "Done",
                AppSoundState.Silent => "none",
                _ => "none"
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 1. Dispose services FIRST to close all database connections
            _singleInstanceCoordinator?.Dispose();
            _singleInstanceCoordinator = null;
            _runtimeCancelHook?.Dispose();
            _runtimeCancelHook = null;
            _trayManager?.Dispose();
            _services?.SystemAudioMuteService?.RestoreAfterRecording();
            _services?.Dispose();

            // 2. NOW create backup (no active connections, file is safe to copy)
            try
            {
                DatabaseInitializer.CreateBackup();
                Logger.Info("App", "Database backup created on exit");
            }
            catch (Exception ex)
            {
                Logger.Warn("App", $"Failed to create database backup on exit: {ex.Message}");
            }

            base.OnExit(e);
        }

        internal void RecordExternalForegroundWindow()
        {
            try
            {
                var context = _services?.WindowManager?.CaptureCurrentWindow();
                if (context != null && context.IsValid() && !IsApplicationWindow(context))
                {
                    _lastWindowContext = context;
                    System.Diagnostics.Debug.WriteLine("[WindowCapture] Last external window context updated from widget interaction.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARNING] Failed to record external window context: {ex.Message}");
            }
        }

        private static bool IsApplicationWindow(WindowContext? context)
        {
            if (context == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(context.ProcessName) &&
                   string.Equals(context.ProcessName, CurrentProcessName, StringComparison.OrdinalIgnoreCase);
        }

        public void ShowSettings()
        {
            ShowMainWindow();
            _mainWindow?.NavigateToSettings();
        }

        public void ShowHistory()
        {
            ShowMainWindow();
            _mainWindow?.NavigateToHistory();
        }

        public void ShowNotes()
        {
            ShowMainWindow();
            _mainWindow?.NavigateToNotes();
        }

        public void ShowDictionary()
        {
            ShowMainWindow();
            _mainWindow?.NavigateToDictionary();
        }

        /// <summary>
        /// Toggles unattended recording via the capsule widget.
        /// Used by Notes page mic button - click to start, click to stop.
        /// </summary>
        public void ToggleCapsuleRecording()
        {
            _capsuleWidget?.ToggleUnattendedRecording();
        }

        /// <summary>
        /// Exposes the capsule widget's state manager for external state observation.
        /// Used by Notes page to sync mic button state with capsule state.
        /// </summary>
        public WidgetStateManager? CapsuleStateManager => _capsuleWidget?.GetStateManager();
    }
}
