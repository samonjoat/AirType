using System;
using System.Diagnostics;
using System.IO;
using AirType.Services.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AirType.Services;

/// <summary>
/// Plays notification sounds for recording state changes.
/// WAV assets are predecoded once at startup so playback is fast and deterministic.
/// </summary>
public sealed class NotificationSoundService : INotificationSoundService, IDisposable
{
    private const string Tag = "NotificationSound";
    private const int TargetSampleRate = 44100;
    private const int TargetChannels = 2;
    private const int SharedModeLatencyMs = 40;

    // Volume multipliers per sound (1.0 = original level), baked into PCM at startup.
    private const float StartVolume = 1.0f;
    private const float DoneVolume = 1.5f;
    private const float CancelVolume = 1.0f;

    private readonly object _syncRoot = new();
    private readonly CachedSound? _startSound;
    private readonly CachedSound? _doneSound;
    private readonly CachedSound? _cancelSound;
    private readonly WaveFormat _playbackFormat;

    private MixingSampleProvider? _mixer;
    private WasapiOut? _outputPlayer;
    private MMDevice? _renderDevice;
    private string? _renderDeviceId;
    private string? _renderDeviceName;
    private volatile bool _disposed;

    public NotificationSoundService()
    {
        var soundsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds");

        _playbackFormat = WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, TargetChannels);

        _startSound = LoadAndDecode(soundsDir, "start.wav", StartVolume);
        _doneSound = LoadAndDecode(soundsDir, "done.wav", DoneVolume);
        _cancelSound = LoadAndDecode(soundsDir, "cancel.wav", CancelVolume);
        EnsureOutputSession("startup");

        Logger.Info(Tag, $"Initialized. Sounds dir: {soundsDir}, " +
            $"start={_startSound != null}, done={_doneSound != null}, cancel={_cancelSound != null}");
    }

    /// <summary>Plays the recording-started notification sound.</summary>
    public void PlayStart() => QueueSound(_startSound, "start");

    /// <summary>
    /// Plays the recording-started notification sound and invokes a callback after playback drains.
    /// </summary>
    public void PlayStart(Action? onCompleted) => QueueSound(_startSound, "start", onCompleted, SharedModeLatencyMs);

    /// <summary>Plays the transcription-complete notification sound.</summary>
    public void PlayDone() => QueueSound(_doneSound, "done");

    /// <summary>Plays the cancel/error notification sound.</summary>
    public void PlayCancel() => QueueSound(_cancelSound, "cancel");

    private void QueueSound(CachedSound? sound, string name, Action? onCompleted = null, int completionDelayMs = 0)
    {
        if (_disposed)
            return;

        if (sound == null)
        {
            Logger.Warn(Tag, $"Skipping {name}: sound asset unavailable");
            InvokeCompletionCallback(onCompleted, 0);
            return;
        }

        var enqueueTimestamp = Stopwatch.GetTimestamp();

        lock (_syncRoot)
        {
            if (_disposed)
                return;

            if (!EnsureOutputSession(name))
            {
                var latency = Stopwatch.GetElapsedTime(enqueueTimestamp);
                Logger.Warn(Tag, $"Skipping {name}: audio output session unavailable after {latency.TotalMilliseconds:F1}ms");
                InvokeCompletionCallback(onCompleted, 0);
                return;
            }

            var queueLatency = Stopwatch.GetElapsedTime(enqueueTimestamp);
            var requestId = Guid.NewGuid().ToString("N")[..8];
            var renderDeviceName = _renderDeviceName ?? "unknown";
            var renderDeviceId = _renderDeviceId ?? "n/a";

            Logger.Info(Tag,
                $"Queueing {name} [{requestId}] on '{renderDeviceName}' " +
                $"({renderDeviceId}) after {queueLatency.TotalMilliseconds:F1}ms");

            _mixer!.AddMixerInput(new CachedSoundSampleProvider(sound, () =>
            {
                Logger.Info(Tag,
                    $"Completed {name} [{requestId}] on '{renderDeviceName}' " +
                    $"({renderDeviceId})");
                InvokeCompletionCallback(onCompleted, completionDelayMs);
            }));
        }
    }

    private void InvokeCompletionCallback(Action? onCompleted, int completionDelayMs)
    {
        if (onCompleted == null)
            return;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                if (completionDelayMs > 0)
                {
                    await System.Threading.Tasks.Task.Delay(completionDelayMs);
                }

                onCompleted();
            }
            catch (Exception ex)
            {
                Logger.Warn(Tag, $"Sound completion callback failed: {ex.Message}");
            }
        });
    }

    private bool EnsureOutputSession(string reason)
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return false;

            var currentDefault = GetCurrentDefaultRenderDeviceInfo();
            bool defaultDeviceChanged = currentDefault != null &&
                                        _renderDeviceId != null &&
                                        !string.Equals(currentDefault.Value.Id, _renderDeviceId, StringComparison.OrdinalIgnoreCase);

            if (_outputPlayer != null && _mixer != null && !defaultDeviceChanged)
            {
                return true;
            }

            if (defaultDeviceChanged)
            {
                Logger.Warn(Tag,
                    $"Default render device changed from '{_renderDeviceName}' ({_renderDeviceId}) " +
                    $"to '{currentDefault!.Value.Name}' ({currentDefault.Value.Id}); reopening audio session");
            }

            DisposeOutputSessionLocked();

            try
            {
                _renderDevice = GetDefaultRenderDevice();
                _renderDeviceId = _renderDevice.ID;
                _renderDeviceName = _renderDevice.FriendlyName;
                _mixer = new MixingSampleProvider(_playbackFormat) { ReadFully = true };
                _outputPlayer = new WasapiOut(_renderDevice, AudioClientShareMode.Shared, true, SharedModeLatencyMs);
                _outputPlayer.PlaybackStopped += OnOutputPlaybackStopped;
                _outputPlayer.Init(_mixer.ToWaveProvider());
                _outputPlayer.Play();

                Logger.Info(Tag,
                    $"Audio output session opened on '{_renderDeviceName}' ({_renderDeviceId}) for {reason} and warmed up");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(Tag, $"Failed to open audio output session for {reason}", ex);
                DisposeOutputSessionLocked();
                return false;
            }
        }
    }

    private void OnOutputPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return;

            if (e.Exception != null)
            {
                Logger.Error(Tag, "Persistent audio session stopped with an output error", e.Exception);
            }
            else
            {
                Logger.Warn(Tag, "Persistent audio session stopped unexpectedly");
            }

            DisposeOutputSessionLocked();
        }
    }

    private MMDevice GetDefaultRenderDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private (string Id, string Name)? GetCurrentDefaultRenderDeviceInfo()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return (device.ID, device.FriendlyName);
        }
        catch (Exception ex)
        {
            Logger.Warn(Tag, $"Unable to query current default render device: {ex.Message}");
            return null;
        }
    }

    private void DisposeOutputSessionLocked()
    {
        if (_outputPlayer != null)
        {
            _outputPlayer.PlaybackStopped -= OnOutputPlaybackStopped;

            try { _outputPlayer.Stop(); } catch { }
            try { _outputPlayer.Dispose(); } catch { }
        }

        if (_renderDevice != null)
        {
            try { _renderDevice.Dispose(); } catch { }
        }

        _outputPlayer = null;
        _mixer = null;
        _renderDevice = null;
        _renderDeviceId = null;
        _renderDeviceName = null;
    }

    private CachedSound? LoadAndDecode(string soundsDir, string fileName, float volume)
    {
        var path = Path.Combine(soundsDir, fileName);
        if (!File.Exists(path))
        {
            Logger.Warn(Tag, $"Sound file not found: {path}");
            return null;
        }

        try
        {
            using var reader = new WaveFileReader(path);

            ISampleProvider samples = reader.ToSampleProvider();

            if (samples.WaveFormat.SampleRate != TargetSampleRate)
            {
                samples = new WdlResamplingSampleProvider(samples, TargetSampleRate);
            }

            if (samples.WaveFormat.Channels == 1)
            {
                samples = new MonoToStereoSampleProvider(samples);
            }

            if (Math.Abs(volume - 1.0f) > 0.001f)
            {
                samples = new VolumeSampleProvider(samples) { Volume = volume };
            }

            var output = new float[Math.Max(_playbackFormat.SampleRate * _playbackFormat.Channels, 4096)];
            int totalSamples = 0;

            while (true)
            {
                int available = output.Length - totalSamples;
                if (available == 0)
                {
                    Array.Resize(ref output, output.Length * 2);
                    available = output.Length - totalSamples;
                }

                int samplesRead = samples.Read(output, totalSamples, available);
                if (samplesRead == 0)
                    break;

                totalSamples += samplesRead;
            }

            Array.Resize(ref output, totalSamples);

            Logger.Info(Tag,
                $"Decoded {fileName} -> PCM {TargetSampleRate}Hz/{TargetChannels}ch/float32 ({totalSamples} samples)");
            return new CachedSound(output, _playbackFormat);
        }
        catch (Exception ex)
        {
            Logger.Error(Tag, $"Failed to decode {fileName}", ex);
            return null;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return;

            _disposed = true;
            DisposeOutputSessionLocked();
        }

        Logger.Info(Tag, "NotificationSoundService disposed");
    }

    private sealed class CachedSound
    {
        public CachedSound(float[] audioData, WaveFormat waveFormat)
        {
            AudioData = audioData;
            WaveFormat = waveFormat;
        }

        public float[] AudioData { get; }

        public WaveFormat WaveFormat { get; }
    }

    private sealed class CachedSoundSampleProvider : ISampleProvider
    {
        private readonly CachedSound _cachedSound;
        private readonly Action? _onCompleted;
        private int _position;
        private bool _completionNotified;

        public CachedSoundSampleProvider(CachedSound cachedSound, Action? onCompleted)
        {
            _cachedSound = cachedSound;
            _onCompleted = onCompleted;
        }

        public WaveFormat WaveFormat => _cachedSound.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int availableSamples = _cachedSound.AudioData.Length - _position;
            int samplesToCopy = Math.Min(availableSamples, count);

            if (samplesToCopy > 0)
            {
                Array.Copy(_cachedSound.AudioData, _position, buffer, offset, samplesToCopy);
                _position += samplesToCopy;
            }

            if (!_completionNotified && _position >= _cachedSound.AudioData.Length)
            {
                _completionNotified = true;
                _onCompleted?.Invoke();
            }

            return samplesToCopy;
        }
    }
}
