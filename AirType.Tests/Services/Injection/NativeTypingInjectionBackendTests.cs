using AirType.Models;
using AirType.Models.Injection;
using AirType.Services;
using AirType.Services.Injection;
using AirType.Services.Injection.Backends;
using Xunit;

namespace AirType.Tests.Services.Injection;

public sealed class NativeTypingInjectionBackendTests
{
    [Fact]
    public void Build_AsciiEmojiAndEmbeddedNewline_ProducesExpectedSequence()
    {
        IReadOnlyList<NativeTextInputEvent> events = NativeTypingSequence.Build("A😀\nB");

        Assert.Equal(
            new[]
            {
                new NativeTextInputEvent(NativeTextInputEventKind.UnicodeKeyDown, 'A'),
                new NativeTextInputEvent(NativeTextInputEventKind.UnicodeKeyUp, 'A'),
                new NativeTextInputEvent(NativeTextInputEventKind.UnicodeKeyDown, 0xD83D),
                new NativeTextInputEvent(NativeTextInputEventKind.UnicodeKeyUp, 0xD83D),
                new NativeTextInputEvent(NativeTextInputEventKind.UnicodeKeyDown, 0xDE00),
                new NativeTextInputEvent(NativeTextInputEventKind.UnicodeKeyUp, 0xDE00),
                new NativeTextInputEvent(NativeTextInputEventKind.VirtualKeyDown, NativeTypingSequence.ReturnVirtualKey),
                new NativeTextInputEvent(NativeTextInputEventKind.VirtualKeyUp, NativeTypingSequence.ReturnVirtualKey),
                new NativeTextInputEvent(NativeTextInputEventKind.UnicodeKeyDown, 'B'),
                new NativeTextInputEvent(NativeTextInputEventKind.UnicodeKeyUp, 'B')
            },
            events);
    }

    [Fact]
    public void Build_DoesNotAppendTrailingNewline()
    {
        IReadOnlyList<NativeTextInputEvent> events = NativeTypingSequence.Build("abc");

        Assert.DoesNotContain(events, inputEvent =>
            inputEvent.Kind is NativeTextInputEventKind.VirtualKeyDown or NativeTextInputEventKind.VirtualKeyUp);
    }

    [Fact]
    public void CanAttempt_FalseWhenToggleOff()
    {
        var backend = new NativeTypingInjectionBackend(new RecordingNativeTextInput());

        Assert.False(backend.CanAttempt(CreateContext(nativeTypingEnabled: false, isForeground: true)));
    }

    [Fact]
    public void CanAttempt_FalseWhenTargetNotForeground()
    {
        var backend = new NativeTypingInjectionBackend(new RecordingNativeTextInput());

        Assert.False(backend.CanAttempt(CreateContext(nativeTypingEnabled: true, isForeground: false)));
    }

    [Fact]
    public async Task TryInjectAsync_WhenEnabledAndForeground_ReturnsProvisional()
    {
        var nativeInput = new RecordingNativeTextInput();
        var backend = new NativeTypingInjectionBackend(nativeInput);

        TextInjectionResult result = await backend.TryInjectAsync(
            CreateContext(nativeTypingEnabled: true, isForeground: true, text: "hi"),
            CancellationToken.None);

        Assert.Equal(TextInjectionOutcome.Provisional, result.Outcome);
        Assert.True(result.RecoverySuggested);
        Assert.Equal("NativeTyping", result.Method);
        Assert.Equal(NativeTypingSequence.Build("hi"), nativeInput.LastEvents);
    }

    private static InjectionContext CreateContext(
        bool nativeTypingEnabled,
        bool isForeground,
        string text = "hello")
    {
        return new InjectionContext(
            new WindowContext(new IntPtr(123), "Target", "target", DateTime.UtcNow),
            text,
            preserveExistingFocus: true,
            new NoopClipboardManager(),
            _ => isForeground,
            () => Task.CompletedTask,
            nativeTypingEnabled);
    }

    private sealed class RecordingNativeTextInput : INativeTextInput
    {
        public IReadOnlyList<NativeTextInputEvent>? LastEvents { get; private set; }

        public bool Send(IReadOnlyList<NativeTextInputEvent> events)
        {
            LastEvents = events;
            return true;
        }
    }

    private sealed class NoopClipboardManager : IClipboardManager
    {
        public Task<ClipboardSnapshot> CaptureSnapshotAsync() => Task.FromResult(ClipboardSnapshot.Empty());

        public Task<bool> RestoreSnapshotAsync(ClipboardSnapshot snapshot) => Task.FromResult(true);

        public Task<bool> SetTextAsync(string text) => Task.FromResult(true);
    }
}
