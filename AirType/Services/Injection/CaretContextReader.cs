using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace AirType.Services.Injection;

internal sealed record CaretContext(
    char? PrecedingCharacter,
    bool IsDocumentEmpty,
    bool IsCaretAtStart,
    string? PrecedingText = null);

internal interface ICaretContextReader
{
    CaretContext? ReadPrecedingContext(IntPtr targetHandle);
}

internal sealed class UIAutomationCaretContextReader : ICaretContextReader
{
    private const int PrecedingContextMaxCharacters = 16;
    private readonly ICaretTextElementResolver _elementResolver;

    public UIAutomationCaretContextReader()
        : this(new UIAutomationCaretTextElementResolver())
    {
    }

    internal UIAutomationCaretContextReader(ICaretTextElementResolver elementResolver)
    {
        _elementResolver = elementResolver ?? throw new ArgumentNullException(nameof(elementResolver));
    }

    public CaretContext? ReadPrecedingContext(IntPtr targetHandle)
    {
        try
        {
            ICaretTextElement? element = _elementResolver.ResolveFocusedElement(targetHandle);
            if (element == null || element.IsPassword || !element.TryGetTextPattern(out ICaretTextPattern? pattern) || pattern == null)
            {
                return null;
            }

            IReadOnlyList<ICaretTextRange> selections = pattern.GetSelection();
            if (selections.Count == 0)
            {
                return null;
            }

            ICaretTextRange selection = selections[0];
            if (!IsDegenerate(selection))
            {
                return null;
            }

            ICaretTextRange documentRange = pattern.DocumentRange;
            bool documentEmpty = string.IsNullOrEmpty(documentRange.GetText(1));
            bool caretAtStart = IsAtDocumentStart(selection, documentRange);
            if (documentEmpty || caretAtStart)
            {
                return new CaretContext(null, documentEmpty, caretAtStart);
            }

            string precedingText = ReadPrecedingText(selection);
            if (precedingText.Length == 0)
            {
                return new CaretContext(null, documentEmpty, true);
            }

            return new CaretContext(precedingText[^1], documentEmpty, false, precedingText);
        }
        catch (Exception ex)
        {
            Logger.Debug("TextInjection", $"Caret context unavailable: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static bool IsDegenerate(ICaretTextRange range) =>
        range.CompareEndpoints(CaretTextRangeEndpoint.Start, range, CaretTextRangeEndpoint.End) == 0;

    private static bool IsAtDocumentStart(ICaretTextRange caretRange, ICaretTextRange documentRange) =>
        caretRange.CompareEndpoints(CaretTextRangeEndpoint.Start, documentRange, CaretTextRangeEndpoint.Start) == 0;

    private static string ReadPrecedingText(ICaretTextRange selection)
    {
        ICaretTextRange probe = selection.Clone();
        var chars = new List<char>(PrecedingContextMaxCharacters);

        for (int i = 0; i < PrecedingContextMaxCharacters; i++)
        {
            if (probe.MoveByCharacter(-1) == 0)
            {
                break;
            }

            probe.ExpandToCharacter();
            string text = probe.GetText(1);
            if (string.IsNullOrEmpty(text))
            {
                break;
            }

            chars.Add(text[0]);
        }

        chars.Reverse();
        return new string(chars.ToArray());
    }
}

internal interface ICaretTextElementResolver
{
    ICaretTextElement? ResolveFocusedElement(IntPtr rootHandle);
}

internal interface ICaretTextElement
{
    bool IsPassword { get; }

    bool TryGetTextPattern(out ICaretTextPattern? pattern);
}

internal interface ICaretTextPattern
{
    IReadOnlyList<ICaretTextRange> GetSelection();

    ICaretTextRange DocumentRange { get; }
}

internal enum CaretTextRangeEndpoint
{
    Start,
    End
}

internal interface ICaretTextRange
{
    ICaretTextRange Clone();

    int MoveByCharacter(int count);

    void ExpandToCharacter();

    string GetText(int maxLength);

    int CompareEndpoints(
        CaretTextRangeEndpoint endpoint,
        ICaretTextRange targetRange,
        CaretTextRangeEndpoint targetEndpoint);
}

internal sealed class UIAutomationCaretTextElementResolver : ICaretTextElementResolver
{
    public ICaretTextElement? ResolveFocusedElement(IntPtr rootHandle)
    {
        if (rootHandle == IntPtr.Zero)
        {
            return null;
        }

        AutomationElement? focused = TryGetFocusedElement();
        return focused != null && BelongsToRoot(rootHandle, focused)
            ? new UIAutomationCaretTextElement(focused)
            : null;
    }

    private static AutomationElement? TryGetFocusedElement()
    {
        try
        {
            return AutomationElement.FocusedElement;
        }
        catch
        {
            return null;
        }
    }

    private static bool BelongsToRoot(IntPtr rootHandle, AutomationElement element)
    {
        AutomationElement? current = element;
        while (current != null)
        {
            try
            {
                IntPtr nativeHandle = new(current.Current.NativeWindowHandle);
                if (nativeHandle == rootHandle || (nativeHandle != IntPtr.Zero && NativeMethods.IsChild(rootHandle, nativeHandle)))
                {
                    return true;
                }

                if (Automation.Compare(current, AutomationElement.RootElement))
                {
                    return false;
                }

                current = TreeWalker.RawViewWalker.GetParent(current);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
    }
}

internal sealed class UIAutomationCaretTextElement : ICaretTextElement
{
    private readonly AutomationElement _element;

    public UIAutomationCaretTextElement(AutomationElement element)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
    }

    public bool IsPassword => GetBoolProperty(AutomationElement.IsPasswordProperty);

    public bool TryGetTextPattern(out ICaretTextPattern? pattern)
    {
        try
        {
            if (_element.TryGetCurrentPattern(TextPattern.Pattern, out object rawPattern) &&
                rawPattern is TextPattern textPattern)
            {
                pattern = new UIAutomationCaretTextPattern(textPattern);
                return true;
            }
        }
        catch
        {
        }

        pattern = null;
        return false;
    }

    private bool GetBoolProperty(AutomationProperty property)
    {
        try
        {
            object value = _element.GetCurrentPropertyValue(property, ignoreDefaultValue: true);
            return value is bool boolValue && boolValue;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class UIAutomationCaretTextPattern : ICaretTextPattern
{
    private readonly TextPattern _pattern;

    public UIAutomationCaretTextPattern(TextPattern pattern)
    {
        _pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
    }

    public IReadOnlyList<ICaretTextRange> GetSelection() =>
        _pattern.GetSelection()
            .Select(range => (ICaretTextRange)new UIAutomationCaretTextRange(range))
            .ToArray();

    public ICaretTextRange DocumentRange => new UIAutomationCaretTextRange(_pattern.DocumentRange);
}

internal sealed class UIAutomationCaretTextRange : ICaretTextRange
{
    private readonly TextPatternRange _range;

    public UIAutomationCaretTextRange(TextPatternRange range)
    {
        _range = range ?? throw new ArgumentNullException(nameof(range));
    }

    public ICaretTextRange Clone() => new UIAutomationCaretTextRange(_range.Clone());

    public int MoveByCharacter(int count) => _range.Move(TextUnit.Character, count);

    public void ExpandToCharacter() => _range.ExpandToEnclosingUnit(TextUnit.Character);

    public string GetText(int maxLength) => _range.GetText(maxLength) ?? string.Empty;

    public int CompareEndpoints(
        CaretTextRangeEndpoint endpoint,
        ICaretTextRange targetRange,
        CaretTextRangeEndpoint targetEndpoint)
    {
        if (targetRange is not UIAutomationCaretTextRange automationTargetRange)
        {
            throw new ArgumentException("Target range must be a UIAutomation caret text range.", nameof(targetRange));
        }

        return _range.CompareEndpoints(
            ToAutomationEndpoint(endpoint),
            automationTargetRange._range,
            ToAutomationEndpoint(targetEndpoint));
    }

    private static TextPatternRangeEndpoint ToAutomationEndpoint(CaretTextRangeEndpoint endpoint) =>
        endpoint == CaretTextRangeEndpoint.Start
            ? TextPatternRangeEndpoint.Start
            : TextPatternRangeEndpoint.End;
}
