using AirType.Services.Injection;
using Xunit;

namespace AirType.Tests.Services.Injection;

public sealed class UIAutomationCaretContextReaderTests
{
    private static readonly IntPtr TargetHandle = new(1234);

    [Fact]
    public void ReadPrecedingContext_WhenTextPatternHasCaret_ReturnsPrecedingCharacter()
    {
        var reader = new UIAutomationCaretContextReader(
            new FakeElementResolver(new FakeElement(new FakeTextPattern("Hello", selectionStart: 5))));

        CaretContext? context = reader.ReadPrecedingContext(TargetHandle);

        Assert.NotNull(context);
        Assert.Equal('o', context.PrecedingCharacter);
        Assert.Equal("Hello", context.PrecedingText);
        Assert.False(context.IsDocumentEmpty);
        Assert.False(context.IsCaretAtStart);
    }

    [Fact]
    public void ReadPrecedingContext_WhenCaretAtStart_ReturnsAtStartContext()
    {
        var reader = new UIAutomationCaretContextReader(
            new FakeElementResolver(new FakeElement(new FakeTextPattern("Hello", selectionStart: 0))));

        CaretContext? context = reader.ReadPrecedingContext(TargetHandle);

        Assert.NotNull(context);
        Assert.Null(context.PrecedingCharacter);
        Assert.False(context.IsDocumentEmpty);
        Assert.True(context.IsCaretAtStart);
    }

    [Fact]
    public void ReadPrecedingContext_WhenDocumentIsEmpty_ReturnsEmptyDocumentContext()
    {
        var reader = new UIAutomationCaretContextReader(
            new FakeElementResolver(new FakeElement(new FakeTextPattern(string.Empty, selectionStart: 0))));

        CaretContext? context = reader.ReadPrecedingContext(TargetHandle);

        Assert.NotNull(context);
        Assert.Null(context.PrecedingCharacter);
        Assert.True(context.IsDocumentEmpty);
        Assert.True(context.IsCaretAtStart);
    }

    [Fact]
    public void ReadPrecedingContext_WhenElementHasNoTextPattern_ReturnsNull()
    {
        var reader = new UIAutomationCaretContextReader(
            new FakeElementResolver(new FakeElement(pattern: null)));

        CaretContext? context = reader.ReadPrecedingContext(TargetHandle);

        Assert.Null(context);
    }

    [Fact]
    public void ReadPrecedingContext_WhenSelectionIsNotCaret_ReturnsNull()
    {
        var reader = new UIAutomationCaretContextReader(
            new FakeElementResolver(new FakeElement(new FakeTextPattern("Hello", selectionStart: 1, selectionEnd: 3))));

        CaretContext? context = reader.ReadPrecedingContext(TargetHandle);

        Assert.Null(context);
    }

    [Fact]
    public void ReadPrecedingContext_WhenTextPatternThrows_ReturnsNull()
    {
        var reader = new UIAutomationCaretContextReader(
            new FakeElementResolver(new FakeElement(new ThrowingTextPattern())));

        CaretContext? context = reader.ReadPrecedingContext(TargetHandle);

        Assert.Null(context);
    }

    [Fact]
    public void ReadPrecedingContext_WhenResolverThrows_ReturnsNull()
    {
        var reader = new UIAutomationCaretContextReader(new ThrowingElementResolver());

        CaretContext? context = reader.ReadPrecedingContext(TargetHandle);

        Assert.Null(context);
    }

    [Fact]
    public void ReadPrecedingContext_WhenTargetIsPassword_ReturnsNull()
    {
        var reader = new UIAutomationCaretContextReader(
            new FakeElementResolver(new FakeElement(new FakeTextPattern("secret", selectionStart: 6), isPassword: true)));

        CaretContext? context = reader.ReadPrecedingContext(TargetHandle);

        Assert.Null(context);
    }

    private sealed class FakeElementResolver : ICaretTextElementResolver
    {
        private readonly ICaretTextElement? _element;

        public FakeElementResolver(ICaretTextElement? element)
        {
            _element = element;
        }

        public ICaretTextElement? ResolveFocusedElement(IntPtr rootHandle) => _element;
    }

    private sealed class ThrowingElementResolver : ICaretTextElementResolver
    {
        public ICaretTextElement? ResolveFocusedElement(IntPtr rootHandle) =>
            throw new InvalidOperationException("UIA unavailable.");
    }

    private sealed class FakeElement : ICaretTextElement
    {
        private readonly ICaretTextPattern? _pattern;

        public FakeElement(ICaretTextPattern? pattern, bool isPassword = false)
        {
            _pattern = pattern;
            IsPassword = isPassword;
        }

        public bool IsPassword { get; }

        public bool TryGetTextPattern(out ICaretTextPattern? pattern)
        {
            pattern = _pattern;
            return pattern != null;
        }
    }

    private sealed class FakeTextPattern : ICaretTextPattern
    {
        private readonly string _text;
        private readonly int _selectionStart;
        private readonly int _selectionEnd;

        public FakeTextPattern(string text, int selectionStart, int? selectionEnd = null)
        {
            _text = text;
            _selectionStart = selectionStart;
            _selectionEnd = selectionEnd ?? selectionStart;
        }

        public IReadOnlyList<ICaretTextRange> GetSelection() =>
            new[] { new FakeTextRange(_text, _selectionStart, _selectionEnd) };

        public ICaretTextRange DocumentRange => new FakeTextRange(_text, 0, _text.Length);
    }

    private sealed class ThrowingTextPattern : ICaretTextPattern
    {
        public IReadOnlyList<ICaretTextRange> GetSelection() =>
            throw new InvalidOperationException("Selection unavailable.");

        public ICaretTextRange DocumentRange => throw new InvalidOperationException("Document unavailable.");
    }

    private sealed class FakeTextRange : ICaretTextRange
    {
        private readonly string _text;
        private int _start;
        private int _end;

        public FakeTextRange(string text, int start, int end)
        {
            _text = text;
            _start = Math.Clamp(start, 0, text.Length);
            _end = Math.Clamp(end, _start, text.Length);
        }

        public ICaretTextRange Clone() => new FakeTextRange(_text, _start, _end);

        public int MoveByCharacter(int count)
        {
            int maxNegativeMove = -_start;
            int maxPositiveMove = _text.Length - _end;
            int actualMove = Math.Clamp(count, maxNegativeMove, maxPositiveMove);
            _start += actualMove;
            _end += actualMove;
            return actualMove;
        }

        public void ExpandToCharacter()
        {
            if (_start < _text.Length)
            {
                _end = _start + 1;
            }
        }

        public string GetText(int maxLength)
        {
            int length = Math.Max(0, Math.Min(_end, _text.Length) - _start);
            if (maxLength >= 0)
            {
                length = Math.Min(length, maxLength);
            }

            return length == 0 ? string.Empty : _text.Substring(_start, length);
        }

        public int CompareEndpoints(
            CaretTextRangeEndpoint endpoint,
            ICaretTextRange targetRange,
            CaretTextRangeEndpoint targetEndpoint)
        {
            if (targetRange is not FakeTextRange fakeTargetRange)
            {
                throw new ArgumentException("Target range must be fake.", nameof(targetRange));
            }

            return GetOffset(endpoint).CompareTo(fakeTargetRange.GetOffset(targetEndpoint));
        }

        private int GetOffset(CaretTextRangeEndpoint endpoint) =>
            endpoint == CaretTextRangeEndpoint.Start ? _start : _end;
    }
}
