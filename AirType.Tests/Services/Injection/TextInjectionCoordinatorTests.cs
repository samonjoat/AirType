using AirType.Models;
using AirType.Models.Injection;
using AirType.Services;
using AirType.Services.Injection;
using AirType.Services.Injection.Backends;
using Xunit;

namespace AirType.Tests.Services.Injection;

public sealed class TextInjectionCoordinatorTests
{
    [Fact]
    public async Task InjectAsync_TriesBackendsInPriorityOrderAndStopsAfterSuccess()
    {
        var first = new FakeBackend("first", InjectionBackendPriority.Win32EditDirect, TextInjectionResult.Failure("first", "failed"));
        var second = new FakeBackend("second", InjectionBackendPriority.ClipboardPaste, TextInjectionResult.SuccessResult("second"));
        var third = new FakeBackend("third", InjectionBackendPriority.ClipboardOnly, TextInjectionResult.SuccessResult("third"));
        var coordinator = new TextInjectionCoordinator(new ITextInjectionBackend[] { third, second, first });

        TextInjectionResult result = await coordinator.InjectAsync(CreateContext());

        Assert.True(result.Success);
        Assert.Equal("second", result.Method);
        Assert.Equal(1, first.Attempts);
        Assert.Equal(1, second.Attempts);
        Assert.Equal(0, third.Attempts);
    }

    [Fact]
    public async Task InjectAsync_ConfirmedOutcomeStopsTheChain()
    {
        var first = new FakeBackend("first", InjectionBackendPriority.ScintillaDirect, TextInjectionResult.SuccessResult("first"));
        var second = new FakeBackend("second", InjectionBackendPriority.ClipboardPaste, TextInjectionResult.SuccessResult("second"));
        var coordinator = new TextInjectionCoordinator(new ITextInjectionBackend[] { second, first });

        TextInjectionResult result = await coordinator.InjectAsync(CreateContext());

        Assert.Equal(TextInjectionOutcome.Confirmed, result.Outcome);
        Assert.Equal("first", result.Method);
        Assert.Equal(1, first.Attempts);
        Assert.Equal(0, second.Attempts);
    }

    [Fact]
    public async Task InjectAsync_ProvisionalOutcomeStopsTheChainWithRecoveryFlagged()
    {
        var first = new FakeBackend("first", InjectionBackendPriority.ScintillaDirect, TextInjectionResult.Failure("first", "failed"));
        var second = new FakeBackend("second", InjectionBackendPriority.ClipboardPaste, TextInjectionResult.ProvisionalResult("second", "dispatched"));
        var third = new FakeBackend("third", InjectionBackendPriority.ClipboardOnly, TextInjectionResult.ClipboardFallback("fallback"));
        var coordinator = new TextInjectionCoordinator(new ITextInjectionBackend[] { third, second, first });

        TextInjectionResult result = await coordinator.InjectAsync(CreateContext());

        Assert.Equal(TextInjectionOutcome.Provisional, result.Outcome);
        Assert.True(result.RecoverySuggested);
        Assert.Equal("second", result.Method);
        Assert.Equal(1, first.Attempts);
        Assert.Equal(1, second.Attempts);
        Assert.Equal(0, third.Attempts);
    }

    [Fact]
    public async Task InjectAsync_FailureOutcomeContinuesTheChain()
    {
        var first = new FakeBackend("first", InjectionBackendPriority.ScintillaDirect, TextInjectionResult.Failure("first", "failed"));
        var second = new FakeBackend("second", InjectionBackendPriority.Win32EditDirect, TextInjectionResult.SuccessResult("second"));
        var coordinator = new TextInjectionCoordinator(new ITextInjectionBackend[] { second, first });

        TextInjectionResult result = await coordinator.InjectAsync(CreateContext());

        Assert.Equal(TextInjectionOutcome.Confirmed, result.Outcome);
        Assert.Equal("second", result.Method);
        Assert.Equal(1, first.Attempts);
        Assert.Equal(1, second.Attempts);
    }

    [Fact]
    public async Task InjectAsync_SkipsBackendsThatCannotAttempt()
    {
        var skipped = new FakeBackend("skipped", InjectionBackendPriority.ScintillaDirect, TextInjectionResult.SuccessResult("skipped"), canAttempt: false);
        var attempted = new FakeBackend("attempted", InjectionBackendPriority.Win32EditDirect, TextInjectionResult.SuccessResult("attempted"));
        var coordinator = new TextInjectionCoordinator(new ITextInjectionBackend[] { skipped, attempted });

        TextInjectionResult result = await coordinator.InjectAsync(CreateContext());

        Assert.True(result.Success);
        Assert.Equal("attempted", result.Method);
        Assert.Equal(0, skipped.Attempts);
        Assert.Equal(1, attempted.Attempts);
    }

    [Fact]
    public async Task InjectAsync_UsesClipboardOnlyBackendAfterEarlierBackendsFail()
    {
        var clipboard = new RecordingClipboardManager();
        var failedDirect = new FakeBackend(
            "direct",
            InjectionBackendPriority.ScintillaDirect,
            TextInjectionResult.Failure("direct", "failed"));
        var failedPaste = new FakeBackend(
            "paste",
            InjectionBackendPriority.ClipboardPaste,
            TextInjectionResult.Failure("paste", "failed"));
        var coordinator = new TextInjectionCoordinator(new ITextInjectionBackend[]
        {
            new ClipboardOnlyInjectionBackend(),
            failedPaste,
            failedDirect
        });

        TextInjectionResult result = await coordinator.InjectAsync(CreateContext(clipboard));

        Assert.True(result.Success);
        Assert.Equal("ClipboardOnly", result.Method);
        Assert.True(result.ClipboardFallbackUsed);
        Assert.False(result.ClipboardFallbackExpected);
        Assert.Equal("Transcription copied to clipboard — press Ctrl+V to paste.", result.Message);
        Assert.Equal("hello", clipboard.LastText);
        Assert.Equal(1, failedDirect.Attempts);
        Assert.Equal(1, failedPaste.Attempts);
    }

    [Fact]
    public async Task ClipboardPaste_WhenTargetChanged_LeavesTextOnClipboardWithoutDispatchingPaste()
    {
        var clipboard = new RecordingClipboardManager();
        int pasteDispatches = 0;
        var backend = new ClipboardPasteInjectionBackend();

        TextInjectionResult result = await backend.TryInjectAsync(
            CreateContext(
                clipboard,
                _ => false,
                () =>
                {
                    pasteDispatches++;
                    return Task.CompletedTask;
                }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.ClipboardFallbackUsed);
        Assert.Equal("ClipboardOnly", result.Method);
        Assert.Equal("Transcription copied to clipboard — press Ctrl+V to paste.", result.Message);
        Assert.Equal("hello", clipboard.LastText);
        Assert.Equal(0, pasteDispatches);
    }

    [Fact]
    public async Task ClipboardPaste_WhenSettled_ReturnsProvisionalNotConfirmed()
    {
        var clipboard = new RecordingClipboardManager();
        int pasteDispatches = 0;
        var backend = new ClipboardPasteInjectionBackend();

        TextInjectionResult result = await backend.TryInjectAsync(
            CreateContext(
                clipboard,
                _ => true,
                () =>
                {
                    pasteDispatches++;
                    return Task.CompletedTask;
                }),
            CancellationToken.None);

        Assert.Equal(TextInjectionOutcome.Provisional, result.Outcome);
        Assert.True(result.RecoverySuggested);
        Assert.Equal("ClipboardPaste", result.Method);
        Assert.Equal("hello", clipboard.LastText);
        Assert.Equal(1, pasteDispatches);
        Assert.Equal(0, clipboard.RestoreAttempts);
    }

    [Fact]
    public async Task ClipboardPaste_WhenSnapshotIsNonText_ReturnsFailureAndChainContinues()
    {
        var clipboard = new RecordingClipboardManager(
            ClipboardSnapshot.CapturedNative(
                new[] { new ClipboardFormatSnapshot(15, "CF_HDROP", new byte[] { 1, 2, 3 }) },
                "file drop"));
        var paste = new ClipboardPasteInjectionBackend();
        var fallback = new FakeBackend("fallback", InjectionBackendPriority.ClipboardOnly, TextInjectionResult.ClipboardFallback("fallback"));
        var coordinator = new TextInjectionCoordinator(new ITextInjectionBackend[] { fallback, paste });

        TextInjectionResult result = await coordinator.InjectAsync(CreateContext(clipboard));

        Assert.Equal(TextInjectionOutcome.ClipboardFallback, result.Outcome);
        Assert.Null(clipboard.LastText);
        Assert.Equal(0, clipboard.RestoreAttempts);
        Assert.Equal(1, fallback.Attempts);
    }

    private static InjectionContext CreateContext(
        IClipboardManager? clipboardManager = null,
        Func<IntPtr, bool>? isForegroundWindow = null,
        Func<Task>? dispatchPasteAsync = null)
    {
        return new InjectionContext(
            new WindowContext(new IntPtr(123), "Target", "target", DateTime.UtcNow),
            "hello",
            preserveExistingFocus: true,
            clipboardManager ?? new NoopClipboardManager(),
            isForegroundWindow ?? (_ => true),
            dispatchPasteAsync ?? (() => Task.CompletedTask));
    }

    private sealed class FakeBackend : ITextInjectionBackend
    {
        private readonly TextInjectionResult _result;
        private readonly bool _canAttempt;

        public FakeBackend(string name, InjectionBackendPriority priority, TextInjectionResult result, bool canAttempt = true)
        {
            Name = name;
            Priority = priority;
            _result = result;
            _canAttempt = canAttempt;
        }

        public string Name { get; }

        public InjectionBackendPriority Priority { get; }

        public int Attempts { get; private set; }

        public bool CanAttempt(InjectionContext context) => _canAttempt;

        public Task<TextInjectionResult> TryInjectAsync(InjectionContext context, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(_result);
        }
    }

    private sealed class NoopClipboardManager : IClipboardManager
    {
        public Task<ClipboardSnapshot> CaptureSnapshotAsync() => Task.FromResult(ClipboardSnapshot.Empty());

        public Task<bool> RestoreSnapshotAsync(ClipboardSnapshot snapshot) => Task.FromResult(true);

        public Task<bool> SetTextAsync(string text) => Task.FromResult(true);
    }

    private sealed class RecordingClipboardManager : IClipboardManager
    {
        private readonly ClipboardSnapshot _snapshot;

        public RecordingClipboardManager()
            : this(ClipboardSnapshot.Empty())
        {
        }

        public RecordingClipboardManager(ClipboardSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public string? LastText { get; private set; }

        public int RestoreAttempts { get; private set; }

        public Task<ClipboardSnapshot> CaptureSnapshotAsync() => Task.FromResult(_snapshot);

        public Task<bool> RestoreSnapshotAsync(ClipboardSnapshot snapshot)
        {
            RestoreAttempts++;
            return Task.FromResult(true);
        }

        public Task<bool> SetTextAsync(string text)
        {
            LastText = text;
            return Task.FromResult(true);
        }
    }
}
