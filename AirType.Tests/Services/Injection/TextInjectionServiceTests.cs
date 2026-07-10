using AirType.Models;
using AirType.Models.Injection;
using AirType.Services;
using AirType.Services.Injection;
using System.Runtime.InteropServices;
using Xunit;

namespace AirType.Tests.Services.Injection;

public sealed class TextInjectionServiceTests
{
    [Fact]
    public async Task InjectTextAsync_WhenSmartInsertionOff_PassesTextUnchangedWithoutReadingCaret()
    {
        var backend = new RecordingBackend();
        var caretReader = new RecordingCaretContextReader(new CaretContext('.', false, false, "Sentence one."));
        var service = CreateService(backend, caretReader, smartInsertionEnabled: false);

        TextInjectionResult result = await service.InjectTextAsync(CreateWindowContext(), "Sentence two.", preserveExistingFocus: true);

        Assert.True(result.Success);
        Assert.Equal("Sentence two.", backend.LastText);
        Assert.Equal(0, caretReader.ReadCalls);
    }

    [Fact]
    public async Task InjectTextAsync_WhenSmartInsertionOnAndContextUnavailable_PassesTextUnchanged()
    {
        var backend = new RecordingBackend();
        var caretReader = new RecordingCaretContextReader(context: null);
        var service = CreateService(backend, caretReader, smartInsertionEnabled: true);

        TextInjectionResult result = await service.InjectTextAsync(CreateWindowContext(), "Sentence two.", preserveExistingFocus: true);

        Assert.True(result.Success);
        Assert.Equal("Sentence two.", backend.LastText);
        Assert.Equal(1, caretReader.ReadCalls);
    }

    [Fact]
    public async Task InjectTextAsync_WhenSmartInsertionOnAndContextReadable_PassesAdjustedTextToCoordinator()
    {
        var backend = new RecordingBackend();
        var caretReader = new RecordingCaretContextReader(new CaretContext('.', false, false, "Sentence one."));
        var service = CreateService(backend, caretReader, smartInsertionEnabled: true);

        TextInjectionResult result = await service.InjectTextAsync(CreateWindowContext(), "sentence two.", preserveExistingFocus: true);

        Assert.True(result.Success);
        Assert.Equal(" Sentence two.", backend.LastText);
        Assert.Equal(1, caretReader.ReadCalls);
    }

    private static TextInjectionService CreateService(
        RecordingBackend backend,
        RecordingCaretContextReader caretReader,
        bool smartInsertionEnabled)
    {
        return new TextInjectionService(
            new NoopClipboardManager(),
            new TextInjectionCoordinator(new ITextInjectionBackend[] { backend }),
            pasteDispatcher: null,
            nativeTypingEnabled: () => false,
            caretContextReader: caretReader,
            smartInsertionEnabled: () => smartInsertionEnabled,
            isForegroundWindow: _ => true);
    }

    private static WindowContext CreateWindowContext() =>
        new(NativeMethods.GetDesktopWindow(), "Target", "target", DateTime.UtcNow);

    private sealed class RecordingBackend : ITextInjectionBackend
    {
        public string Name => "Recording";

        public InjectionBackendPriority Priority => InjectionBackendPriority.ScintillaDirect;

        public string? LastText { get; private set; }

        public bool CanAttempt(InjectionContext context) => true;

        public Task<TextInjectionResult> TryInjectAsync(InjectionContext context, CancellationToken cancellationToken)
        {
            LastText = context.Text;
            return Task.FromResult(TextInjectionResult.SuccessResult(Name));
        }
    }

    private sealed class RecordingCaretContextReader : ICaretContextReader
    {
        private readonly CaretContext? _context;

        public RecordingCaretContextReader(CaretContext? context)
        {
            _context = context;
        }

        public int ReadCalls { get; private set; }

        public CaretContext? ReadPrecedingContext(IntPtr targetHandle)
        {
            ReadCalls++;
            return _context;
        }
    }

    private sealed class NoopClipboardManager : IClipboardManager
    {
        public Task<ClipboardSnapshot> CaptureSnapshotAsync() => Task.FromResult(ClipboardSnapshot.Empty());

        public Task<bool> RestoreSnapshotAsync(ClipboardSnapshot snapshot) => Task.FromResult(true);

        public Task<bool> SetTextAsync(string text) => Task.FromResult(true);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();
    }
}
