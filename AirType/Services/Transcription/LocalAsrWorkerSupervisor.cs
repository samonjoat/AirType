using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services.Configuration;
using AirType.Services.Dictionary;

namespace AirType.Services.Transcription;

public sealed class LocalAsrWorkerSupervisor : ILocalAsrWorkerSupervisor
{
    private static readonly TimeSpan WarmupTimeout = TimeSpan.FromSeconds(60);

    private readonly IOfflineEngineManager _offlineEngineManager;
    private readonly ICredentialManager _credentialManager;
    private readonly ILocalAsrWorkerClient _client;
    private readonly IDictionaryHotwordBuilder _hotwordBuilder;
    private readonly IRollingInitialPromptBuilder _promptBuilder;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Process? _workerProcess;
    private string? _authToken;
    private int _port;
    private bool _disposed;
    private LocalAsrOptions? _activeWorkerOptions;
    private Task? _warmupTask;
    private TaskCompletionSource<bool>? _prewarmCompletion;

    public LocalAsrWorkerSupervisor(
        IOfflineEngineManager offlineEngineManager,
        ICredentialManager credentialManager,
        ILocalAsrWorkerClient client,
        IDictionaryHotwordBuilder hotwordBuilder,
        IRollingInitialPromptBuilder promptBuilder,
        LocalAsrOptions? options = null)
    {
        _offlineEngineManager = offlineEngineManager ?? throw new ArgumentNullException(nameof(offlineEngineManager));
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _hotwordBuilder = hotwordBuilder ?? throw new ArgumentNullException(nameof(hotwordBuilder));
        _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        _activeWorkerOptions = options;
        _client.WorkerEventReceived += OnWorkerEventReceived;
    }

    public LocalAsrWorkerState State { get; private set; } = LocalAsrWorkerState.Stopped;

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        var options = ResolveActiveOptions();
        await StartCt2WorkerAsync(options, cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        PrewarmActiveModelAsync(cancellationToken);

    public async Task PrewarmActiveModelAsync(CancellationToken cancellationToken)
    {
        var options = ResolveActiveOptions();
        await StartCt2WorkerAsync(options, cancellationToken);
        var warmupTask = _warmupTask;
        if (warmupTask != null)
        {
            await warmupTask.WaitAsync(cancellationToken);
        }
    }

    private async Task StartCt2WorkerAsync(LocalAsrOptions options, CancellationToken cancellationToken)
    {
        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (_client.IsConnected &&
                _activeWorkerOptions != null &&
                string.Equals(_activeWorkerOptions.ModelId, options.ModelId, StringComparison.Ordinal))
            {
                BeginWarmupIfNeeded(options);
                return;
            }

            if (_client.IsConnected)
            {
                await ShutdownAsync(cancellationToken);
            }

            var status = _offlineEngineManager.GetStatus(options.ModelId);
            if (!status.IsInstalled)
            {
                State = LocalAsrWorkerState.Unavailable;
                Logger.Warn("LocalAsrWorkerSupervisor", $"Local ASR worker is not installed: {status.StatusText}");
                return;
            }

            State = LocalAsrWorkerState.Starting;
            _authToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            _port = GetAvailableLoopbackPort();

            _activeWorkerOptions = options;
            KillStaleWorkerProcesses(status.WorkerPath, exceptProcessId: null);
            Logger.Info("LocalAsrWorkerSupervisor", $"Starting CT2 local ASR worker for {options.ModelId} on port {_port}.");
            _workerProcess = StartWorkerProcess(status.WorkerPath, status.ModelPath, options, _port, _authToken);
            var endpoint = new Uri($"ws://127.0.0.1:{_port}/");
            await ConnectWithRetryAsync(endpoint, _authToken, cancellationToken);

            State = LocalAsrWorkerState.Ready;
            BeginWarmupIfNeeded(options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            State = LocalAsrWorkerState.Faulted;
            Logger.Warn("LocalAsrWorkerSupervisor", $"Failed to start local ASR worker: {ex.Message}");
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<ILocalAsrSession?> TryStartSessionAsync(Guid recordingId, CancellationToken cancellationToken)
    {
        var options = ResolveActiveOptions();
        var status = _offlineEngineManager.GetStatus(options.ModelId);
        if (!status.IsInstalled)
        {
            State = LocalAsrWorkerState.Unavailable;
            Logger.Warn("LocalAsrWorkerSupervisor", $"Selected local ASR model is not installed: {status.ModelDisplayName} ({status.StatusText})");
            return null;
        }

        await EnsureStartedAsync(cancellationToken);
        if (!_client.IsConnected)
        {
            return null;
        }

        if (State is LocalAsrWorkerState.Faulted or LocalAsrWorkerState.Unavailable)
        {
            Logger.Warn("LocalAsrWorkerSupervisor", $"Local ASR session rejected because worker state is {State}.");
            return null;
        }

        var ct2Hotwords = _hotwordBuilder.BuildHotwords(options.HotwordTokenCap);
        var ct2Session = new LocalAsrSession(recordingId, _client, options, _promptBuilder);
        await ct2Session.StartAsync(ct2Hotwords.Text, ct2Hotwords.TokenCount, cancellationToken);
        return ct2Session;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        _warmupTask = null;
        _prewarmCompletion?.TrySetCanceled(cancellationToken);
        _prewarmCompletion = null;

        try
        {
            if (_client.IsConnected)
            {
                await _client.ShutdownAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("LocalAsrWorkerSupervisor", $"Worker shutdown request failed: {ex.Message}");
        }

        if (_workerProcess != null && !_workerProcess.HasExited)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
                await _workerProcess.WaitForExitAsync(timeoutCts.Token);
            }
            catch
            {
                try
                {
                    _workerProcess.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            }
        }

        if (_workerProcess != null)
        {
            try
            {
                KillStaleWorkerProcesses(
                    _workerProcess.StartInfo.WorkingDirectory,
                    _workerProcess.Id);
            }
            catch
            {
            }
        }

        State = LocalAsrWorkerState.Stopped;
        _activeWorkerOptions = null;
        _workerProcess?.Dispose();
        _workerProcess = null;
        Logger.Info("LocalAsrWorkerSupervisor", "CT2 local ASR worker stopped.");
    }

    private void BeginWarmupIfNeeded(LocalAsrOptions options)
    {
        if (_warmupTask is { IsCompleted: false } &&
            _activeWorkerOptions != null &&
            string.Equals(_activeWorkerOptions.ModelId, options.ModelId, StringComparison.Ordinal))
        {
            return;
        }

        if (State == LocalAsrWorkerState.Prewarmed &&
            _activeWorkerOptions != null &&
            string.Equals(_activeWorkerOptions.ModelId, options.ModelId, StringComparison.Ordinal))
        {
            return;
        }

        _prewarmCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _warmupTask = Task.Run(() => LoadAndPrewarmAsync(options, CancellationToken.None));
    }

    private async Task LoadAndPrewarmAsync(LocalAsrOptions options, CancellationToken cancellationToken)
    {
        try
        {
            State = LocalAsrWorkerState.ModelLoading;
            Logger.Info("LocalAsrWorkerSupervisor", $"Warming CT2 local ASR model {options.ModelId}.");
            await _client.LoadModelAsync(options, cancellationToken);
            await _client.PrewarmAsync(cancellationToken);
            var completion = _prewarmCompletion;
            if (completion != null)
            {
                await completion.Task.WaitAsync(WarmupTimeout, cancellationToken);
            }

            Logger.Info("LocalAsrWorkerSupervisor", $"CT2 local ASR model {options.ModelId} is prewarmed.");
        }
        catch (TimeoutException)
        {
            State = LocalAsrWorkerState.Faulted;
            Logger.Warn("LocalAsrWorkerSupervisor", $"Local ASR model warmup timed out after {WarmupTimeout.TotalSeconds:F0}s.");
        }
        catch (Exception ex)
        {
            State = LocalAsrWorkerState.Faulted;
            Logger.Warn("LocalAsrWorkerSupervisor", $"Local ASR model warmup failed: {ex.Message}");
        }
    }

    private Process StartWorkerProcess(string workerPath, string modelPath, LocalAsrOptions options, int port, string authToken)
    {
        string python = ResolvePythonExecutable(workerPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = workerPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("airtype_asr_worker");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add("--auth-token");
        startInfo.ArgumentList.Add(authToken);
        startInfo.ArgumentList.Add("--model-dir");
        startInfo.ArgumentList.Add(modelPath);
        startInfo.ArgumentList.Add("--cpu-threads");
        startInfo.ArgumentList.Add(options.CpuThreads.ToString());

        startInfo.Environment["PYTHONPATH"] = workerPath;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch local ASR worker process.");

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Logger.Debug("LocalAsrWorker", e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Logger.Warn("LocalAsrWorker", e.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private async Task ConnectWithRetryAsync(Uri endpoint, string authToken, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _client.ConnectAsync(endpoint, authToken, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                await Task.Delay(125, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Local ASR worker did not accept a WebSocket connection: {lastError?.Message}");
    }

    private void OnWorkerEventReceived(object? sender, LocalAsrWorkerEventArgs e)
    {
        var workerEvent = e.WorkerEvent;
        LogWorkerEvent(workerEvent);

        State = workerEvent.Type switch
        {
            "ready" => LocalAsrWorkerState.Ready,
            "model_loading" => LocalAsrWorkerState.ModelLoading,
            "model_loaded" => LocalAsrWorkerState.ModelLoaded,
            "prewarmed" => LocalAsrWorkerState.Prewarmed,
            "error" when !IsWarningEvent(workerEvent) => LocalAsrWorkerState.Faulted,
            _ => State
        };

        if (workerEvent.Type == "prewarmed")
        {
            _prewarmCompletion?.TrySetResult(true);
        }
        else if (workerEvent.Type == "error" && !IsWarningEvent(workerEvent))
        {
            _prewarmCompletion?.TrySetException(new InvalidOperationException(
                $"{workerEvent.Code ?? "WORKER_ERROR"}: {workerEvent.Message ?? "Local ASR worker error"}"));
        }
    }

    private static void LogWorkerEvent(LocalAsrProtocol.WorkerEvent workerEvent)
    {
        string details = workerEvent.Type switch
        {
            "model_loading" => $"model={workerEvent.Model ?? "unknown"}",
            "model_loaded" => $"model={workerEvent.Model ?? "unknown"} loadMs={workerEvent.LoadMilliseconds?.ToString() ?? "n/a"}",
            "prewarmed" => $"prewarmMs={workerEvent.PrewarmMilliseconds?.ToString() ?? "n/a"}",
            "recording_started" => $"recordingId={workerEvent.RecordingId?.ToString() ?? "n/a"}",
            "utterance_final" => $"recordingId={workerEvent.RecordingId?.ToString() ?? "n/a"} index={workerEvent.Index?.ToString() ?? "n/a"} asrMs={workerEvent.AsrMilliseconds?.ToString() ?? "n/a"} textLength={workerEvent.Text?.Length ?? 0}",
            "recording_complete" => $"recordingId={workerEvent.RecordingId?.ToString() ?? "n/a"} utterances={workerEvent.UtteranceCount?.ToString() ?? "n/a"} finalFlushMs={workerEvent.FinalFlushMilliseconds?.ToString() ?? "n/a"} asrTotalMs={workerEvent.AsrTotalMilliseconds?.ToString() ?? "n/a"} textLength={workerEvent.Text?.Length ?? 0}",
            "metrics" => $"recordingId={workerEvent.RecordingId?.ToString() ?? "n/a"} name={workerEvent.Name ?? "n/a"} value={workerEvent.Value?.ToString() ?? "n/a"} hotwords={workerEvent.HotwordTokenCount?.ToString() ?? "n/a"} utterances={workerEvent.UtteranceCount?.ToString() ?? "n/a"}",
            "error" => $"severity={workerEvent.Severity ?? "error"} code={workerEvent.Code ?? "n/a"} message={workerEvent.Message ?? "n/a"} recordingId={workerEvent.RecordingId?.ToString() ?? "n/a"}",
            _ => string.Empty
        };

        string message = string.IsNullOrWhiteSpace(details)
            ? $"Worker event: {workerEvent.Type}"
            : $"Worker event: {workerEvent.Type} | {details}";

        if (workerEvent.Type == "error" && !IsWarningEvent(workerEvent))
        {
            Logger.Warn("LocalAsrWorkerSupervisor", message);
        }
        else
        {
            Logger.Debug("LocalAsrWorkerSupervisor", message);
        }
    }

    private static bool IsWarningEvent(LocalAsrProtocol.WorkerEvent workerEvent) =>
        string.Equals(workerEvent.Severity, "warning", StringComparison.OrdinalIgnoreCase);

    private LocalAsrOptions ResolveActiveOptions()
    {
        string activeModelId = _credentialManager.GetModelId(TranscriptionProvider.Local);
        var model = LocalAsrModelCatalog.GetRequired(activeModelId);
        return LocalAsrOptions.FromModel(model);
    }

    private static int GetAvailableLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string ResolvePythonExecutable(string workerPath)
    {
        string? configured = Environment.GetEnvironmentVariable("AIRTYPE_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string runtimeRoot = Path.Combine(
            Directory.GetParent(workerPath)?.FullName ?? workerPath,
            OfflineEngineManager.PythonRuntimeDirectoryName);
        string runtimePython = OfflineEngineManager.ResolveRuntimePythonPath(runtimeRoot);
        return File.Exists(runtimePython) ? runtimePython : "python";
    }

    private static void KillStaleWorkerProcesses(string workerPath, int? exceptProcessId)
    {
        string expectedPythonPath = ResolvePythonExecutable(workerPath);
        if (string.Equals(expectedPythonPath, "python", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var process in Process.GetProcessesByName("python").Concat(Process.GetProcessesByName("pythonw")))
        {
            try
            {
                if (exceptProcessId.HasValue && process.Id == exceptProcessId.Value)
                {
                    continue;
                }

                string? processPath = process.MainModule?.FileName;
                if (!string.Equals(processPath, expectedPythonPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Logger.Warn("LocalAsrWorkerSupervisor", $"Killing stale CT2 worker process {process.Id} at {processPath}.");
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Logger.Warn("LocalAsrWorkerSupervisor", $"Failed to inspect or kill stale CT2 worker process {process.Id}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _client.WorkerEventReceived -= OnWorkerEventReceived;
        await ShutdownAsync(CancellationToken.None);
        _disposed = true;
        await _client.DisposeAsync();
        _workerProcess?.Dispose();
        _startLock.Dispose();
    }
}
