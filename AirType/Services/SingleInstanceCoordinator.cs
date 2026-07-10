using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AirType.Services
{
    internal sealed class SingleInstanceCoordinator : IDisposable
    {
        private const string MutexName = @"Local\AirType.SingleInstance";
        private const string PipeName = "AirType.SingleInstance";
        private const string ActivateCommand = "show";

        private readonly object _syncRoot = new();
        private readonly CancellationTokenSource _listenerCancellation = new();

        private Mutex? _mutex;
        private Task? _listenerTask;
        private Action? _activationRequested;
        private bool _ownsMutex;
        private bool _startupComplete;
        private bool _pendingActivation;
        private bool _disposed;

        public event Action? ActivationRequested
        {
            add
            {
                lock (_syncRoot)
                {
                    ThrowIfDisposed();
                    _activationRequested += value;
                }
            }
            remove
            {
                lock (_syncRoot)
                {
                    _activationRequested -= value;
                }
            }
        }

        public bool TryAcquirePrimaryInstance()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();

                _mutex = new Mutex(false, MutexName);

                try
                {
                    if (!_mutex.WaitOne(0, false))
                    {
                        _mutex.Dispose();
                        _mutex = null;
                        return false;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // The previous primary exited unexpectedly. Treat this process as primary.
                }

                _ownsMutex = true;
                _listenerTask = Task.Run(ListenForActivationRequestsAsync);
                return true;
            }
        }

        public async Task<bool> SignalPrimaryInstanceAsync()
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                    await client.ConnectAsync(750, CancellationToken.None).ConfigureAwait(false);

                    using var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: false)
                    {
                        AutoFlush = true
                    };

                    await writer.WriteLineAsync(ActivateCommand).ConfigureAwait(false);
                    return true;
                }
                catch (IOException)
                {
                    // The primary process may still be starting its pipe listener.
                }
                catch (TimeoutException)
                {
                    // Retry briefly to cover launches racing primary startup.
                }

                await Task.Delay(150).ConfigureAwait(false);
            }

            return false;
        }

        public void MarkStartupComplete()
        {
            Action? activationRequested = null;

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _startupComplete = true;

                if (_pendingActivation)
                {
                    _pendingActivation = false;
                    activationRequested = _activationRequested;
                }
            }

            activationRequested?.Invoke();
        }

        private async Task ListenForActivationRequestsAsync()
        {
            try
            {
                while (!_listenerCancellation.IsCancellationRequested)
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(_listenerCancellation.Token).ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: false);
                    var command = await reader.ReadLineAsync().ConfigureAwait(false);

                    if (string.Equals(command, ActivateCommand, StringComparison.OrdinalIgnoreCase))
                    {
                        QueueActivationRequest();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch (ObjectDisposedException)
            {
                // Expected during shutdown.
            }
        }

        private void QueueActivationRequest()
        {
            Action? activationRequested = null;

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                if (_startupComplete)
                {
                    activationRequested = _activationRequested;
                }
                else
                {
                    _pendingActivation = true;
                }
            }

            activationRequested?.Invoke();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SingleInstanceCoordinator));
            }
        }

        public void Dispose()
        {
            Task? listenerTask;

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                listenerTask = _listenerTask;
                _activationRequested = null;
                _pendingActivation = false;
                _startupComplete = true;
            }

            _listenerCancellation.Cancel();

            try
            {
                listenerTask?.Wait(1000);
            }
            catch (AggregateException)
            {
                // Ignore shutdown races.
            }
            catch (ObjectDisposedException)
            {
            }

            _listenerCancellation.Dispose();

            if (_ownsMutex && _mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Ignore if the mutex is no longer owned.
                }

                _mutex.Dispose();
            }

            _mutex = null;
            _listenerTask = null;
        }
    }
}
