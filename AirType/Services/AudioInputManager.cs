using NAudio.Wave;
using NAudio.CoreAudioApi;
using AirType.Models;
using AirType.Models.Transcription;
using AirType.Services.Audio;
using System.IO;
using System.Collections.Generic;

namespace AirType.Services;

/// <summary>
/// Manages audio input capture using NAudio with real-time processing
/// </summary>
public class AudioInputManager : IAudioInputManager
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveFileWriter;
    private RecordingSession? _currentSession;
    private bool _isRecording;
    private readonly object _lockObject = new();
    private readonly IFilePersistenceManager _filePersistenceManager;
    private readonly IRecordingValidityGuard _recordingValidityGuard;
    private readonly IAudioLevelAnalyzer _audioLevelAnalyzer;
    private readonly IAudioFileValidator _audioFileValidator;
    private readonly IRecordingMonitor _recordingMonitor;
    private string? _lastErrorMessage;
    private int? _preferredDeviceNumber;
    private int _lastUsedDeviceNumber;
    private long _pcmFrameSequence;
    private static readonly TimeSpan MaxRecordingDuration = TimeSpan.FromMinutes(5);

    // Phase 3 optimization: In-memory audio buffer to avoid file read after recording
    private MemoryStream? _audioBuffer;
    private byte[]? _completedAudioData;

    // Track where the current recording originated from
    private RecordingSource _currentRecordingSource = RecordingSource.Main;

    // Audio format configuration: 16 kHz, 16-bit, mono
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;

    public event EventHandler<WaveformDataEventArgs>? WaveformDataAvailable;
    public event EventHandler<AudioPcmFrameEventArgs>? PcmFrameAvailable;
    public event EventHandler<RecordingStateEventArgs>? RecordingStateChanged;
    public event EventHandler<SilentAudioDetectedEventArgs>? SilentAudioDetected;

    /// <summary>
    /// Fired when a recording is rejected for being too short.
    /// </summary>
    public event EventHandler<ShortRecordingRejectedEventArgs>? ShortRecordingRejected;

    /// <summary>
    /// Edge Case #9: Fired when audio is detected but is very quiet (may not transcribe well)
    /// </summary>
    public event EventHandler<QuietAudioDetectedEventArgs>? QuietAudioDetected;
    
    /// <summary>
    /// Edge Case #6: Fired when microphone disconnection is detected during recording
    /// </summary>
    public event EventHandler? MicrophoneDisconnected;

    /// <summary>
    /// Edge Case #4: Fired when recording is about to reach max duration (warning)
    /// </summary>
    public event EventHandler<MaxDurationWarningEventArgs>? MaxDurationWarning;

    /// <summary>
    /// Edge Case #4: Fired when recording reaches max duration and is auto-stopped
    /// </summary>
    public event EventHandler<MaxDurationReachedEventArgs>? MaxDurationReached;

    /// <summary>
    /// Edge Case #15: Fired when audio file is corrupted or invalid
    /// </summary>
    public event EventHandler<AudioFileCorruptedEventArgs>? AudioFileCorrupted;

    public string? LastErrorMessage
    {
        get
        {
            lock (_lockObject)
            {
                return _lastErrorMessage;
            }
        }
    }

    public bool IsRecording
    {
        get
        {
            lock (_lockObject)
            {
                return _isRecording;
            }
        }
        private set
        {
            lock (_lockObject)
            {
                _isRecording = value;
            }
        }
    }

    /// <summary>
    /// Gets the current recording session, if any
    /// </summary>
    public RecordingSession? CurrentSession
    {
        get
        {
            lock (_lockObject)
            {
                return _currentSession;
            }
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            var name = caps.ProductName?.TrimEnd('\0').Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Device {i}";
            }
            devices.Add(new AudioDeviceInfo(i, name!, caps.Channels));
        }
        return devices;
    }

    public int? PreferredDeviceNumber
    {
        get
        {
            lock (_lockObject)
            {
                return _preferredDeviceNumber;
            }
        }
    }

    public int LastUsedDeviceNumber
    {
        get
        {
            lock (_lockObject)
            {
                return _lastUsedDeviceNumber;
            }
        }
    }

    public void SetPreferredDevice(int? deviceNumber)
    {
        lock (_lockObject)
        {
            if (deviceNumber.HasValue)
            {
                if (deviceNumber.Value < 0 || deviceNumber.Value >= WaveIn.DeviceCount)
                {
                    _preferredDeviceNumber = null;
                    return;
                }
            }

            _preferredDeviceNumber = deviceNumber;
        }
    }

    /// <summary>
    /// Initializes a new instance of AudioInputManager
    /// </summary>
    /// <param name="filePersistenceManager">File persistence manager for handling file operations</param>
    /// <param name="recordingValidityGuard">Recording validity guard for validating recordings</param>
    /// <param name="audioLevelAnalyzer">Audio level analyzer for RMS/dB calculations</param>
    /// <param name="audioFileValidator">Audio file validator for WAV validation</param>
    /// <param name="recordingMonitor">Recording monitor for timeout/duration tracking</param>
    public AudioInputManager(
        IFilePersistenceManager filePersistenceManager,
        IRecordingValidityGuard recordingValidityGuard,
        IAudioLevelAnalyzer audioLevelAnalyzer,
        IAudioFileValidator audioFileValidator,
        IRecordingMonitor recordingMonitor)
    {
        _filePersistenceManager = filePersistenceManager ?? throw new ArgumentNullException(nameof(filePersistenceManager));
        _recordingValidityGuard = recordingValidityGuard ?? throw new ArgumentNullException(nameof(recordingValidityGuard));
        _audioLevelAnalyzer = audioLevelAnalyzer ?? throw new ArgumentNullException(nameof(audioLevelAnalyzer));
        _audioFileValidator = audioFileValidator ?? throw new ArgumentNullException(nameof(audioFileValidator));
        _recordingMonitor = recordingMonitor ?? throw new ArgumentNullException(nameof(recordingMonitor));

        // Wire up recording monitor events
        _recordingMonitor.MicrophoneDisconnected += OnRecordingMonitorMicrophoneDisconnected;
        _recordingMonitor.MaxDurationWarning += OnRecordingMonitorMaxDurationWarning;
        _recordingMonitor.MaxDurationReached += OnRecordingMonitorMaxDurationReached;
    }

    private void OnRecordingMonitorMicrophoneDisconnected(object? sender, EventArgs e)
    {
        Logger.Warn("AudioInputManager", "Microphone disconnected detected by monitor");
        
        // Stop recording and notify
        Task.Run(async () =>
        {
            try
            {
                await StopRecordingAsync();
                MicrophoneDisconnected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioMonitor] Error handling microphone disconnect: {ex.Message}");
            }
        });
    }

    private void OnRecordingMonitorMaxDurationWarning(object? sender, TimeSpan remaining)
    {
        Logger.Warn("AudioInputManager", $"Max duration warning: {remaining.TotalSeconds:F0} seconds remaining");
        
        // Create the rich event args expected by the interface
        var currentDuration = _currentSession != null
            ? DateTime.UtcNow - _currentSession.StartTime.ToUniversalTime()
            : TimeSpan.Zero;
        
        MaxDurationWarning?.Invoke(this, new MaxDurationWarningEventArgs(currentDuration, MaxRecordingDuration, remaining));
    }

    private void OnRecordingMonitorMaxDurationReached(object? sender, EventArgs e)
    {
        Logger.Warn("AudioInputManager", "Max recording duration reached");
        
        var mode = _currentSession?.Mode ?? RecordingMode.Hotkey;
        
        // Stop recording and notify
        Task.Run(async () =>
        {
            try
            {
                await StopRecordingAsync();
                MaxDurationReached?.Invoke(this, new MaxDurationReachedEventArgs(MaxRecordingDuration, mode));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioMonitor] Error handling max duration: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Starts audio recording with the specified mode
    /// </summary>
    /// <param name="mode">Recording mode (Hotkey or Unattended)</param>
    /// <returns>True if recording started successfully, false otherwise</returns>
    public async Task<bool> StartRecordingAsync(RecordingMode mode, RecordingSource source = RecordingSource.Main)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioInputManager));

        // Track the source of this recording
        _currentRecordingSource = source;

        try
        {
            await Task.Yield();

            lock (_lockObject)
            {
                _lastErrorMessage = null;
            }

            if (IsRecording)
            {
                return false; // Already recording
            }

            // Ensure directory structure exists
            if (!_filePersistenceManager.EnsureDirectoryExists())
            {
                lock (_lockObject)
                {
                    _lastErrorMessage = _filePersistenceManager.LastErrorMessage ??
                        "AirType could not access the %LOCALAPPDATA%\\AirType folder.";
                }
                System.Diagnostics.Debug.WriteLine("Failed to create or access audio directory");
                return false;
            }

            // Check disk space before starting
            if (!_filePersistenceManager.HasSufficientDiskSpace())
            {
                lock (_lockObject)
                {
                    _lastErrorMessage = _filePersistenceManager.LastErrorMessage ??
                        "Not enough free disk space to start recording.";
                }
                System.Diagnostics.Debug.WriteLine("Insufficient disk space for recording");
                return false;
            }

            // Create recording session
            try
            {
                _currentSession = _filePersistenceManager.CreateRecordingSession(mode);
            }
            catch
            {
                lock (_lockObject)
                {
                    _lastErrorMessage = _filePersistenceManager.LastErrorMessage ??
                        "Unable to prepare the recording file under %LOCALAPPDATA%.";
                }
                return false;
            }

            // Initialize microphone with error handling
            if (!InitializeMicrophone())
            {
                lock (_lockObject)
                {
                    _lastErrorMessage ??= "Microphone access was denied. Check Windows privacy settings.";
                }
                _currentSession = null;
                return false;
            }

            // Setup file writer
            if (!InitializeFileWriter())
            {
                lock (_lockObject)
                {
                    _lastErrorMessage ??= "Unable to create the WAV file. Please confirm AirType can write to %LOCALAPPDATA%\\AirType.";
                }
                CleanupResources();
                return false;
            }

            // Phase 3 optimization: Initialize in-memory buffer for audio data
            // This allows us to avoid reading the file from disk after recording completes
            lock (_lockObject)
            {
                _completedAudioData = null;
                _audioBuffer?.Dispose();
                _audioBuffer = new MemoryStream();
                _pcmFrameSequence = 0;
            }

            // Reset audio level tracking for silent detection
            _audioLevelAnalyzer.Reset();

            _recordingMonitor.SessionStartTime = _currentSession.StartTime.ToUniversalTime();
            _recordingMonitor.CurrentMode = mode;

            // Start recording
            _waveIn!.StartRecording();
            IsRecording = true;

            // Edge Case #3: Structured logging
            Logger.Info("AudioInputManager", "Recording started", new { Mode = mode, DeviceNumber = _lastUsedDeviceNumber });

            // Start recording monitor for microphone timeout and max duration
            _recordingMonitor.Start();
            Logger.Debug("AudioInputManager", "[AudioMonitor] Recording monitoring started");

            // Notify state change
            RecordingStateChanged?.Invoke(this, new RecordingStateEventArgs(true, mode, _currentRecordingSource));

            return true;
        }
        catch (Exception ex)
        {
            // Log error and cleanup
            System.Diagnostics.Debug.WriteLine($"Error starting recording: {ex.Message}");
            lock (_lockObject)
            {
                _lastErrorMessage ??= $"Unexpected error while starting recording: {ex.Message}";
            }
            CleanupResources();
            return false;
        }
    }

    /// <summary>
    /// Stops recording and saves the file
    /// </summary>
    public async Task StopRecordingAsync()
    {
        if (_disposed)
            return; // Silently return if disposed

        try
        {
            if (!IsRecording || _currentSession == null)
            {
                return;
            }

            var mode = _currentSession.Mode;

            // Stop recording
            _waveIn?.StopRecording();
            IsRecording = false;

            // Stop recording monitor
            _recordingMonitor.Stop();
            Logger.Debug("AudioInputManager", "[AudioMonitor] Recording monitoring stopped");

            // Brief wait for any pending audio buffers to be processed by NAudio.
            // Reduced from 100ms to 25ms - NAudio's internal buffering handles most of this.
            await Task.Delay(25);

            // Close file writer to finalize the WAV file
            _waveFileWriter?.Dispose();
            _waveFileWriter = null;

            // Check if recording is silent before completing
            bool isSilent = _audioLevelAnalyzer.IsRecordingSilent();
            
            if (isSilent)
            {
                Logger.Warn("AudioMonitor", $"Silent audio detected - RMS: {_audioLevelAnalyzer.GetRmsDb():F2} dB");
                
                // Mark session as cancelled (silent)
                _filePersistenceManager.CompleteRecordingSession(_currentSession, RecordingStatus.Cancelled);
                
                DeleteRecordingFileSafe(_currentSession.FilePath, "[AudioRecorder] Silent audio");
                
                // Notify about silent audio
                SilentAudioDetected?.Invoke(this, new SilentAudioDetectedEventArgs(_audioLevelAnalyzer.GetRmsDb()));
                
                // Notify state change
                RecordingStateChanged?.Invoke(this, new RecordingStateEventArgs(false, mode, _currentRecordingSource));
                
                // Clean up and return
                CleanupAudioResources();
                _currentSession = null;
                return;
            }

            // Edge Case #9: Check if recording is very quiet (but not silent)
            bool isQuiet = _audioLevelAnalyzer.IsRecordingQuiet();
            
            if (isQuiet)
            {
                var rmsDb = _audioLevelAnalyzer.GetRmsDb();
                System.Diagnostics.Debug.WriteLine($"[AudioMonitor] Quiet audio detected - RMS: {rmsDb:F2} dB");
                
                // Notify about quiet audio (but continue with transcription)
                QuietAudioDetected?.Invoke(this, new QuietAudioDetectedEventArgs(rmsDb));
                
                // Note: We don't return here - we let the recording proceed to transcription
                // The user will be warned, but they can still try to transcribe
            }

            var actualDuration = DateTime.UtcNow - _currentSession.StartTime;
            if (actualDuration < TimeSpan.Zero)
            {
                actualDuration = TimeSpan.Zero;
            }

            if (_recordingValidityGuard.ShouldDiscard(_currentSession, actualDuration))
            {
                System.Diagnostics.Debug.WriteLine($"[AudioRecorder] Hotkey recording rejected (duration: {actualDuration.TotalMilliseconds:F0} ms)");
                _filePersistenceManager.CompleteRecordingSession(_currentSession, RecordingStatus.Cancelled);
                DeleteRecordingFileSafe(_currentSession.FilePath, "[AudioRecorder] Short recording");

                ShortRecordingRejected?.Invoke(this, new ShortRecordingRejectedEventArgs(
                    mode,
                    actualDuration,
                    _recordingValidityGuard.MinimumHotkeyDuration));

                RecordingStateChanged?.Invoke(this, new RecordingStateEventArgs(false, mode, _currentRecordingSource));
                CleanupAudioResources();
                _currentSession = null;
                return;
            }

            // Edge Case #15: Validate audio file before completing
            var validationResult = _audioFileValidator.Validate(_currentSession.FilePath);
            
            if (!validationResult.IsValid)
            {
                Logger.Error("AudioInputManager", "[AudioValidator] Audio file validation failed", null, new { ErrorMessage = validationResult.ErrorMessage, FilePath = _currentSession.FilePath });
                
                // Mark session as error
                _filePersistenceManager.CompleteRecordingSession(_currentSession, RecordingStatus.Error);
                
                // Delete corrupted file
                DeleteRecordingFileSafe(_currentSession.FilePath, "[AudioValidator] Corrupted audio file");
                
                // Notify about corruption
                var errorMessage = validationResult.ErrorMessage ?? "Audio file validation failed.";
                AudioFileCorrupted?.Invoke(this, new AudioFileCorruptedEventArgs(errorMessage));
                
                // Notify state change
                RecordingStateChanged?.Invoke(this, new RecordingStateEventArgs(false, mode, _currentRecordingSource));
                
                // Clean up and return
                CleanupAudioResources();
                _currentSession = null;
                return;
            }

            // Complete the recording session (file is valid)
            _filePersistenceManager.CompleteRecordingSession(_currentSession, RecordingStatus.Completed);

            // Phase 3 optimization: Finalize in-memory buffer as complete WAV file
            // This allows the transcription workflow to use the data directly without disk read
            FinalizeAudioBuffer();

            Logger.Info("AudioInputManager", "Recording completed successfully", new 
            { 
                FileSizeBytes = validationResult.FileSizeBytes,
                RmsDb = _audioLevelAnalyzer.GetRmsDb(),
                Samples = _audioLevelAnalyzer.TotalSamplesProcessed,
                Duration = (DateTime.UtcNow - _currentSession.StartTime).TotalSeconds
            });

            // Notify state change
            RecordingStateChanged?.Invoke(this, new RecordingStateEventArgs(false, mode, _currentRecordingSource));

            // Clean up audio resources but keep the file
            CleanupAudioResources();
            _currentSession = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error stopping recording: {ex.Message}");
            
            // Mark session as error if it exists
            if (_currentSession != null)
            {
                _filePersistenceManager.CompleteRecordingSession(_currentSession, RecordingStatus.Error);
            }
            
            CleanupResources();
        }
    }

    /// <summary>
    /// Cancels recording and deletes the partial file
    /// </summary>
    public async Task CancelRecordingAsync()
    {
        if (_disposed)
            return; // Silently return if disposed

        try
        {
            if (!IsRecording || _currentSession == null)
            {
                return;
            }

            var mode = _currentSession.Mode;
            var sessionToCleanup = _currentSession; // Preserve session reference

            // Stop recording
            _waveIn?.StopRecording();
            IsRecording = false;

            // Stop recording monitor
            _recordingMonitor.Stop();
            Logger.Debug("AudioInputManager", "[AudioMonitor] Recording monitoring stopped (cancel)");

            // Wait a moment for any pending operations
            await Task.Delay(100);

            // Dispose file writer and audio resources (but preserve session reference)
            _waveFileWriter?.Dispose();
            _waveFileWriter = null;
            CleanupAudioResources();

            // Delete the file while we still have the session reference
            _filePersistenceManager.CompleteRecordingSession(sessionToCleanup, RecordingStatus.Cancelled);

            // Now null out the session
            _currentSession = null;

            // Notify state change
            RecordingStateChanged?.Invoke(this, new RecordingStateEventArgs(false, mode, _currentRecordingSource));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cancelling recording: {ex.Message}");

            // Mark session as error if it exists
            if (_currentSession != null)
            {
                var sessionToCleanup = _currentSession;
                _currentSession = null;

                // Dispose resources before deleting file
                _waveFileWriter?.Dispose();
                _waveFileWriter = null;
                CleanupAudioResources();

                _filePersistenceManager.CompleteRecordingSession(sessionToCleanup, RecordingStatus.Error);
            }
            else
            {
                // Just cleanup resources if session is already null
                _waveFileWriter?.Dispose();
                _waveFileWriter = null;
                CleanupAudioResources();
            }
        }
    }

    /// <summary>
    /// Initializes the microphone with proper error handling
    /// </summary>
    private bool InitializeMicrophone()
    {
        try
        {
            if (WaveIn.DeviceCount == 0)
            {
                lock (_lockObject)
                {
                    _lastErrorMessage = "No recording devices are available.";
                }
                return false;
            }

            int deviceNumber = GetPreferredDeviceNumber();
            var waveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels);

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = waveFormat,
                BufferMilliseconds = 50, // 50 ms blocks for a smoother, slower waveform update cadence
                NumberOfBuffers = 4
            };

            lock (_lockObject)
            {
                _lastUsedDeviceNumber = deviceNumber;
            }

            var selectedCaps = WaveIn.GetCapabilities(deviceNumber);
            System.Diagnostics.Debug.WriteLine($"Recording device selected: {selectedCaps.ProductName} (index {deviceNumber})");

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("Microphone access denied");
            lock (_lockObject)
            {
                _lastErrorMessage = "Microphone access was denied. Check Windows privacy settings under Settings > Privacy & security > Microphone.";
            }
            return false;
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Microphone initialization failed: {ex.Message}");
            lock (_lockObject)
            {
                _lastErrorMessage = "Microphone is already in use by another application.";
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unexpected error initializing microphone: {ex.Message}");
            lock (_lockObject)
            {
                _lastErrorMessage = "Unexpected error initializing the microphone.";
            }
            return false;
        }
    }

    private int GetPreferredDeviceNumber()
    {
        int? preferred;
        lock (_lockObject)
        {
            preferred = _preferredDeviceNumber;
        }

        if (preferred.HasValue)
        {
            if (preferred.Value >= 0 && preferred.Value < WaveIn.DeviceCount)
            {
                return preferred.Value;
            }

            // Preferred device no longer available
            lock (_lockObject)
            {
                _preferredDeviceNumber = null;
            }
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice? device = null;

            try
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            }
            catch
            {
                // ignore and try communications role
            }

            if (device == null)
            {
                try
                {
                    device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                }
                catch
                {
                    // ignore
                }
            }

            if (device != null)
            {
                var friendly = device.FriendlyName;
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    var caps = WaveIn.GetCapabilities(i);
                    var waveName = caps.ProductName?.TrimEnd('\0').Trim();

                    if (IsNameMatch(waveName, friendly))
                    {
                        return i;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error detecting default capture device: {ex.Message}");
        }

        return 0;
    }

    private static bool IsNameMatch(string? waveName, string? friendlyName)
    {
        if (string.IsNullOrWhiteSpace(waveName) || string.IsNullOrWhiteSpace(friendlyName))
            return false;

        waveName = waveName.Trim();
        friendlyName = friendlyName.Trim();

        if (friendlyName.StartsWith(waveName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (waveName.StartsWith(friendlyName, StringComparison.OrdinalIgnoreCase))
            return true;

        return friendlyName.IndexOf(waveName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Initializes the WAV file writer
    /// </summary>
    private bool InitializeFileWriter()
    {
        try
        {
            if (_currentSession == null || string.IsNullOrEmpty(_currentSession.FilePath) || _waveIn == null)
            {
                return false;
            }

            // Validate the file path for security
            if (!_filePersistenceManager.ValidateFilePath(_currentSession.FilePath))
            {
                System.Diagnostics.Debug.WriteLine($"Invalid file path: {_currentSession.FilePath}");
                return false;
            }

            // Create WAV file writer
            _waveFileWriter = new WaveFileWriter(_currentSession.FilePath, _waveIn.WaveFormat);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing file writer: {ex.Message}");
            lock (_lockObject)
            {
                _lastErrorMessage = "Could not open the recording file for writing.";
            }
            return false;
        }
    }

    /// <summary>
    /// Handles incoming audio data for real-time processing
    /// Edge Case #6: Includes microphone monitoring for disconnection detection
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            // Edge Case #6: Check if we're receiving actual audio data
            bool hasAudioSignal = _audioLevelAnalyzer.HasAudioSignal(e.Buffer, e.BytesRecorded);
            if (hasAudioSignal)
            {
                // Notify recording monitor about audio reception
                _recordingMonitor.OnAudioReceived();
            }

            // Write audio data to file
            // Note: We don't call Flush() here - NAudio and OS buffer writes efficiently.
            // Flushing every ~50ms buffer causes unnecessary disk I/O overhead.
            // The file will be properly flushed when disposed in StopRecordingAsync.
            _waveFileWriter?.Write(e.Buffer, 0, e.BytesRecorded);

            // Phase 3 optimization: Also buffer audio data in memory
            // This avoids needing to read the file back from disk after recording
            lock (_lockObject)
            {
                _audioBuffer?.Write(e.Buffer, 0, e.BytesRecorded);
            }

            // Calculate amplitude for waveform visualization and track audio levels
            var amplitude = _audioLevelAnalyzer.ProcessBuffer(e.Buffer, e.BytesRecorded);
            
            // Notify subscribers with waveform data
            WaveformDataAvailable?.Invoke(this, new WaveformDataEventArgs(amplitude));

            PublishPcmFrame(e.Buffer, e.BytesRecorded);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing audio data: {ex.Message}");
        }
    }

    private void PublishPcmFrame(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded <= 0)
        {
            return;
        }

        RecordingSession? session;
        long sequence;
        lock (_lockObject)
        {
            session = _currentSession;
            sequence = _pcmFrameSequence++;
        }

        if (session == null)
        {
            return;
        }

        var frameBuffer = new byte[bytesRecorded];
        Buffer.BlockCopy(buffer, 0, frameBuffer, 0, bytesRecorded);
        var frame = new AudioPcmFrame(
            session.Id,
            sequence,
            frameBuffer,
            bytesRecorded,
            DateTimeOffset.UtcNow);

        PcmFrameAvailable?.Invoke(this, new AudioPcmFrameEventArgs(frame));
    }

    /// <summary>
    /// Handles recording stopped event
    /// </summary>
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            System.Diagnostics.Debug.WriteLine($"Recording stopped with error: {e.Exception.Message}");
        }
    }

    /// <summary>
    /// Cleans up audio resources only
    /// </summary>
    private void CleanupAudioResources()
    {
        try
        {
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
                _waveIn = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cleaning up audio resources: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up all resources including file writer
    /// </summary>
    private void CleanupResources()
    {
        try
        {
            _waveFileWriter?.Dispose();
            _waveFileWriter = null;
            
            CleanupAudioResources();
            
            // Clear audio buffer on cleanup
            _audioBuffer?.Dispose();
            _audioBuffer = null;
            _completedAudioData = null;
            
            _currentSession = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cleaning up resources: {ex.Message}");
        }
    }

    private bool _disposed = false;

    /// <summary>
    /// Disposes of all resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method for proper disposal pattern
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Stop recording immediately if active
                if (IsRecording)
                {
                    try
                    {
                        // Synchronous stop for disposal - don't use async in Dispose
                        _waveIn?.StopRecording();
                        IsRecording = false;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error stopping recording during disposal: {ex.Message}");
                    }
                }
                
                // Stop recording monitor
                _recordingMonitor.Stop();

                // Unwire recording monitor events
                _recordingMonitor.MicrophoneDisconnected -= OnRecordingMonitorMicrophoneDisconnected;
                _recordingMonitor.MaxDurationWarning -= OnRecordingMonitorMaxDurationWarning;
                _recordingMonitor.MaxDurationReached -= OnRecordingMonitorMaxDurationReached;
                
                // Clean up all resources
                CleanupResources();
            }
            
            _disposed = true;
        }
    }

    /// <summary>
    /// Finalizer to ensure resources are cleaned up if Dispose is not called
    /// </summary>
    ~AudioInputManager()
    {
        Dispose(false);
    }

    private static void DeleteRecordingFileSafe(string? filePath, string debugContext)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                System.Diagnostics.Debug.WriteLine($"{debugContext}: Deleted audio file {filePath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{debugContext}: Failed to delete audio file - {ex.Message}");
        }
    }

    #region Phase 3 Optimization: In-Memory Audio Buffer

    /// <summary>
    /// Finalizes the in-memory audio buffer by constructing a complete WAV file in memory.
    /// Called after recording completes successfully and validation passes.
    /// </summary>
    private void FinalizeAudioBuffer()
    {
        lock (_lockObject)
        {
            if (_audioBuffer == null || _audioBuffer.Length == 0)
            {
                _completedAudioData = null;
                return;
            }

            try
            {
                // Get raw PCM data from buffer
                byte[] pcmData = _audioBuffer.ToArray();

                // Construct a complete WAV file in memory
                using var wavStream = new MemoryStream();
                var waveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels);
                using (var writer = new WaveFileWriter(wavStream, waveFormat))
                {
                    writer.Write(pcmData, 0, pcmData.Length);
                }

                _completedAudioData = wavStream.ToArray();
                Logger.Debug("AudioInputManager", $"[AudioBuffer] Finalized in-memory WAV: {_completedAudioData.Length:N0} bytes");
            }
            catch (Exception ex)
            {
                Logger.Warn("AudioInputManager", $"[AudioBuffer] Failed to finalize buffer: {ex.Message}");
                _completedAudioData = null;
            }
            finally
            {
                // Dispose the raw PCM buffer, we now have the complete WAV
                _audioBuffer?.Dispose();
                _audioBuffer = null;
            }
        }
    }

    /// <summary>
    /// Gets the buffered audio data from the last completed recording, if available.
    /// Returns null if no buffered data is available (caller should fall back to file read).
    /// This is an optimization to avoid reading the file from disk when data is already in memory.
    /// </summary>
    public byte[]? GetBufferedAudioData()
    {
        lock (_lockObject)
        {
            return _completedAudioData;
        }
    }

    /// <summary>
    /// Clears the in-memory audio buffer. Call after transcription is complete to free memory.
    /// </summary>
    public void ClearAudioBuffer()
    {
        lock (_lockObject)
        {
            _completedAudioData = null;
            _audioBuffer?.Dispose();
            _audioBuffer = null;
        }
    }

    #endregion
}
