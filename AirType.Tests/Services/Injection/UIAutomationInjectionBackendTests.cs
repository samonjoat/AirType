using AirType.Models;
using AirType.Models.Injection;
using AirType.Services;
using AirType.Services.Injection;
using AirType.Services.Injection.Backends;
using Xunit;

namespace AirType.Tests.Services.Injection;

public sealed class UIAutomationInjectionBackendTests
{
    [Fact]
    public async Task TryInjectAsync_EmptyWritableValuePatternWithMatchingReadback_ReturnsConfirmed()
    {
        var target = new FakeAutomationTarget(value: string.Empty);
        var backend = new UIAutomationInjectionBackend(new FakeAutomationResolver(target));

        TextInjectionResult result = await backend.TryInjectAsync(CreateContext("hello"), CancellationToken.None);

        Assert.Equal(TextInjectionOutcome.Confirmed, result.Outcome);
        Assert.Equal("UIAutomationInsert", result.Method);
        Assert.Equal("hello", target.Value);
        Assert.Equal(1, target.SetValueCalls);
    }

    [Fact]
    public async Task TryInjectAsync_PopulatedValuePattern_ReturnsFailureWithoutOverwrite()
    {
        var target = new FakeAutomationTarget(value: "existing");
        var backend = new UIAutomationInjectionBackend(new FakeAutomationResolver(target));

        TextInjectionResult result = await backend.TryInjectAsync(CreateContext("hello"), CancellationToken.None);

        Assert.Equal(TextInjectionOutcome.Failure, result.Outcome);
        Assert.Equal("existing", target.Value);
        Assert.Equal(0, target.SetValueCalls);
    }

    [Fact]
    public async Task TryInjectAsync_ReadOnlyValuePattern_ReturnsFailure()
    {
        var target = new FakeAutomationTarget(value: string.Empty, isReadOnly: true);
        var backend = new UIAutomationInjectionBackend(new FakeAutomationResolver(target));

        TextInjectionResult result = await backend.TryInjectAsync(CreateContext("hello"), CancellationToken.None);

        Assert.Equal(TextInjectionOutcome.Failure, result.Outcome);
        Assert.Equal(0, target.SetValueCalls);
    }

    [Fact]
    public async Task TryInjectAsync_NoWritablePattern_ReturnsFailure()
    {
        var target = new FakeAutomationTarget(value: string.Empty, supportsValuePattern: false, supportsTextPattern: false);
        var backend = new UIAutomationInjectionBackend(new FakeAutomationResolver(target));

        TextInjectionResult result = await backend.TryInjectAsync(CreateContext("hello"), CancellationToken.None);

        Assert.Equal(TextInjectionOutcome.Failure, result.Outcome);
        Assert.Equal(0, target.SetValueCalls);
    }

    [Fact]
    public async Task TryInjectAsync_ReadbackMismatch_ReturnsFailure()
    {
        var target = new FakeAutomationTarget(value: string.Empty, readbackOverrideAfterSet: "different");
        var backend = new UIAutomationInjectionBackend(new FakeAutomationResolver(target));

        TextInjectionResult result = await backend.TryInjectAsync(CreateContext("hello"), CancellationToken.None);

        Assert.Equal(TextInjectionOutcome.Failure, result.Outcome);
        Assert.Equal(1, target.SetValueCalls);
    }

    private static InjectionContext CreateContext(string text)
    {
        return new InjectionContext(
            new WindowContext(new IntPtr(123), "Target", "target", DateTime.UtcNow),
            text,
            preserveExistingFocus: true,
            new NoopClipboardManager(),
            _ => true,
            () => Task.CompletedTask);
    }

    private sealed class FakeAutomationResolver : IUIAutomationTextTargetResolver
    {
        private readonly IUIAutomationTextTarget? _target;

        public FakeAutomationResolver(IUIAutomationTextTarget? target)
        {
            _target = target;
        }

        public IUIAutomationTextTarget? ResolveTarget(IntPtr rootHandle) => _target;
    }

    private sealed class FakeAutomationTarget : IUIAutomationTextTarget
    {
        private readonly string? _readbackOverride;

        public FakeAutomationTarget(
            string value,
            bool supportsValuePattern = true,
            bool supportsTextPattern = false,
            bool isReadOnly = false,
            string? readbackOverrideAfterSet = null)
        {
            Value = value;
            SupportsValuePattern = supportsValuePattern;
            SupportsTextPattern = supportsTextPattern;
            IsValueReadOnly = isReadOnly;
            _readbackOverride = readbackOverrideAfterSet;
        }

        public string Description => "fake";

        public bool IsEnabled { get; init; } = true;

        public bool IsKeyboardFocusable { get; init; } = true;

        public bool IsPassword { get; init; }

        public bool SupportsValuePattern { get; }

        public bool IsValueReadOnly { get; }

        public bool SupportsTextPattern { get; }

        public string Value { get; private set; }

        public int SetValueCalls { get; private set; }

        public string GetValue() => SetValueCalls > 0 ? _readbackOverride ?? Value : Value;

        public void SetValue(string value)
        {
            SetValueCalls++;
            Value = value;
        }
    }

    private sealed class NoopClipboardManager : IClipboardManager
    {
        public Task<ClipboardSnapshot> CaptureSnapshotAsync() => Task.FromResult(ClipboardSnapshot.Empty());

        public Task<bool> RestoreSnapshotAsync(ClipboardSnapshot snapshot) => Task.FromResult(true);

        public Task<bool> SetTextAsync(string text) => Task.FromResult(true);
    }
}
