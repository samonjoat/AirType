using System.Windows.Automation;

namespace AirType.Services.Injection;

internal interface IUIAutomationTextTarget
{
    string Description { get; }

    bool IsEnabled { get; }

    bool IsKeyboardFocusable { get; }

    bool IsPassword { get; }

    bool SupportsValuePattern { get; }

    bool IsValueReadOnly { get; }

    bool SupportsTextPattern { get; }

    string GetValue();

    void SetValue(string value);
}

internal interface IUIAutomationTextTargetResolver
{
    IUIAutomationTextTarget? ResolveTarget(IntPtr rootHandle);
}

internal sealed class UIAutomationTextTargetResolver : IUIAutomationTextTargetResolver
{
    public IUIAutomationTextTarget? ResolveTarget(IntPtr rootHandle)
    {
        if (rootHandle == IntPtr.Zero)
        {
            return null;
        }

        AutomationElement? focused = TryGetFocusedElement();
        if (focused != null && BelongsToRoot(rootHandle, focused))
        {
            return new UIAutomationTextTarget(focused);
        }

        AutomationElement? root = TryGetElementFromHandle(rootHandle);
        return root == null ? null : new UIAutomationTextTarget(root);
    }

    private static AutomationElement? TryGetFocusedElement()
    {
        try
        {
            return AutomationElement.FocusedElement;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static AutomationElement? TryGetElementFromHandle(IntPtr handle)
    {
        try
        {
            return AutomationElement.FromHandle(handle);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
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
            catch (ElementNotAvailableException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    private sealed class UIAutomationTextTarget : IUIAutomationTextTarget
    {
        private readonly AutomationElement _element;

        public UIAutomationTextTarget(AutomationElement element)
        {
            _element = element;
        }

        public string Description => SafeName();

        public bool IsEnabled => GetBoolProperty(AutomationElement.IsEnabledProperty);

        public bool IsKeyboardFocusable => GetBoolProperty(AutomationElement.IsKeyboardFocusableProperty);

        public bool IsPassword => GetBoolProperty(AutomationElement.IsPasswordProperty);

        public bool SupportsValuePattern => TryGetValuePattern(out _);

        public bool IsValueReadOnly
        {
            get
            {
                if (!TryGetValuePattern(out ValuePattern? pattern) || pattern == null)
                {
                    return false;
                }

                return pattern.Current.IsReadOnly;
            }
        }

        public bool SupportsTextPattern => TryGetTextPattern(out _);

        public string GetValue()
        {
            if (!TryGetValuePattern(out ValuePattern? pattern) || pattern == null)
            {
                return string.Empty;
            }

            return pattern.Current.Value ?? string.Empty;
        }

        public void SetValue(string value)
        {
            if (!TryGetValuePattern(out ValuePattern? pattern) || pattern == null)
            {
                throw new InvalidOperationException("ValuePattern is not available.");
            }

            pattern.SetValue(value);
        }

        private string SafeName()
        {
            try
            {
                string controlType = _element.Current.ControlType.ProgrammaticName;
                string name = _element.Current.Name;
                return string.IsNullOrWhiteSpace(name) ? controlType : $"{controlType} '{name}'";
            }
            catch
            {
                return "<unavailable>";
            }
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

        private bool TryGetValuePattern(out ValuePattern? pattern)
        {
            try
            {
                if (_element.TryGetCurrentPattern(ValuePattern.Pattern, out object rawPattern) &&
                    rawPattern is ValuePattern valuePattern)
                {
                    pattern = valuePattern;
                    return true;
                }
            }
            catch
            {
            }

            pattern = null;
            return false;
        }

        private bool TryGetTextPattern(out TextPattern? pattern)
        {
            try
            {
                if (_element.TryGetCurrentPattern(TextPattern.Pattern, out object rawPattern) &&
                    rawPattern is TextPattern textPattern)
                {
                    pattern = textPattern;
                    return true;
                }
            }
            catch
            {
            }

            pattern = null;
            return false;
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
    }
}
