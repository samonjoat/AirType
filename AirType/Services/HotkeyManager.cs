using System;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Input;

namespace AirType.Services;

/// <summary>
/// Manages global hotkey registration and handling using Win32 API
/// </summary>
public class HotkeyManager : IHotkeyManager
{
    #region Win32 API Declarations

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    #endregion

    #region Modifier Keys Constants
    
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    #endregion

    private readonly IHotkeyNativeMethods _nativeMethods;
    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private bool _isHotkeyRegistered = false;
    private bool _isHotkeyPressed = false;
    private Keys _registeredKey = Keys.Space;
    private System.Windows.Input.ModifierKeys _registeredModifiers = System.Windows.Input.ModifierKeys.Control;
    private System.Windows.Threading.DispatcherTimer? _keyStateTimer;
    private bool _disposed = false;
    private bool _isSuppressed = false;

    public HotkeyManager(IHotkeyNativeMethods? nativeMethods = null)
    {
        _nativeMethods = nativeMethods ?? new Win32HotkeyNativeMethods();
    }

    /// <summary>
    /// Event fired when hotkey is pressed down
    /// </summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>
    /// Event fired when hotkey is released
    /// </summary>
    public event EventHandler? HotkeyReleased;

    /// <summary>
    /// Event fired when hotkey registration fails
    /// </summary>
    public event EventHandler<HotkeyRegistrationFailedEventArgs>? HotkeyRegistrationFailed;

    /// <summary>
    /// Event fired when the registered hotkey changes
    /// </summary>
    public event EventHandler? HotkeyChanged;

    /// <summary>
    /// Whether a hotkey is currently registered
    /// </summary>
    public bool IsHotkeyRegistered => _isHotkeyRegistered;

    /// <summary>
    /// Whether the hotkey is currently pressed
    /// </summary>
    public bool IsHotkeyPressed => _isHotkeyPressed;

    /// <summary>
    /// When true, hotkey press/release events are suppressed.
    /// Used to prevent hotkey triggers during active workflow processing.
    /// </summary>
    public bool IsSuppressed
    {
        get => _isSuppressed;
        set => _isSuppressed = value;
    }

    /// <summary>
    /// Currently registered key
    /// </summary>
    public Keys RegisteredKey => _registeredKey;

    /// <summary>
    /// Currently registered modifier keys
    /// </summary>
    public System.Windows.Input.ModifierKeys RegisteredModifiers => _registeredModifiers;

    /// <summary>
    /// Initialize the hotkey manager with a window handle
    /// </summary>
    /// <param name="window">WPF window to receive hotkey messages</param>
    public void Initialize(Window window)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HotkeyManager));

        if (window == null)
            throw new ArgumentNullException(nameof(window));

        // Get window handle
        var windowInteropHelper = new WindowInteropHelper(window);
        _windowHandle = windowInteropHelper.Handle;

        if (_windowHandle == IntPtr.Zero)
        {
            // Window not yet created, wait for it
            window.SourceInitialized += (s, e) =>
            {
                _windowHandle = new WindowInteropHelper(window).Handle;
                SetupMessageHook();
            };
        }
        else
        {
            SetupMessageHook();
        }

        // Initialize key state monitoring timer
        _keyStateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50) // Check every 50ms
        };
        _keyStateTimer.Tick += CheckKeyState;
    }

    /// <summary>
    /// Register a global hotkey
    /// </summary>
    /// <param name="key">Key to register</param>
    /// <param name="modifiers">Modifier keys</param>
    /// <returns>True if registration successful, false otherwise</returns>
    public bool RegisterHotkey(Keys key, System.Windows.Input.ModifierKeys modifiers = System.Windows.Input.ModifierKeys.Control)
    {
        if (_disposed)
            return false;

        if (_windowHandle == IntPtr.Zero)
        {
            var args = new HotkeyRegistrationFailedEventArgs(
                "Window handle not available. Hotkey manager must be initialized with a window before registration.", key, modifiers);
            HotkeyRegistrationFailed?.Invoke(this, args);
            return false;
        }

        // Unregister existing hotkey if any
        if (_isHotkeyRegistered)
        {
            UnregisterHotkey();
        }

        try
        {
            // Modifier-only hotkeys (Keys.None) can't use RegisterHotKey
            // because Win32 requires a non-modifier VK code. Instead we
            // skip the native call and rely on GetAsyncKeyState polling
            // for press/release detection (which handles all combos).
            if (key == Keys.None)
            {
                if (modifiers == System.Windows.Input.ModifierKeys.None)
                {
                    HotkeyRegistrationFailed?.Invoke(this, new HotkeyRegistrationFailedEventArgs(
                        "No key or modifier specified", key, modifiers));
                    return false;
                }

                _isHotkeyRegistered = true;
                _registeredKey = key;
                _registeredModifiers = modifiers;
                _keyStateTimer?.Start();
                HotkeyChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            uint modifierFlags = ConvertModifierKeys(modifiers);
            uint virtualKey = (uint)key;

            // Add NOREPEAT flag to prevent repeated WM_HOTKEY messages
            modifierFlags |= MOD_NOREPEAT;

            bool success = _nativeMethods.RegisterHotKey(_windowHandle, HOTKEY_ID, modifierFlags, virtualKey);

            if (success)
            {
                _isHotkeyRegistered = true;
                _registeredKey = key;
                _registeredModifiers = modifiers;

                // Start monitoring key state for press/release detection
                _keyStateTimer?.Start();

                // Notify subscribers that the hotkey has changed
                HotkeyChanged?.Invoke(this, EventArgs.Empty);

                return true;
            }
            else
            {
                var error = _nativeMethods.GetLastError();
                string errorMessage = error switch
                {
                    1409 => "Hotkey already registered by another application",
                    87 => "Invalid parameter",
                    _ => $"Registration failed with error code: {error}"
                };

                var args = new HotkeyRegistrationFailedEventArgs(errorMessage, key, modifiers);
                HotkeyRegistrationFailed?.Invoke(this, args);

                // Also notify change (even if failed, to reset UI state if needed)
                HotkeyChanged?.Invoke(this, EventArgs.Empty);

                return false;
            }
        }
            catch (Exception ex)
            {
                HotkeyRegistrationFailed?.Invoke(this, new HotkeyRegistrationFailedEventArgs(
                    $"Exception during registration: {ex.Message}", key, modifiers));
                return false;
        }
    }

    /// <summary>
    /// Unregister the current hotkey
    /// </summary>
    public void UnregisterHotkey()
    {
        if (_disposed || !_isHotkeyRegistered || _windowHandle == IntPtr.Zero)
            return;

        try
        {
            _nativeMethods.UnregisterHotKey(_windowHandle, HOTKEY_ID);
            _isHotkeyRegistered = false;
            _isHotkeyPressed = false;
            
            // Stop monitoring key state
            _keyStateTimer?.Stop();
        }
        catch (Exception)
        {
            // Ignore errors during unregistration
        }
    }

    /// <summary>
    /// Try to register a hotkey with fallback options if the primary fails
    /// </summary>
    /// <param name="primaryKey">Primary key to try</param>
    /// <param name="primaryModifiers">Primary modifiers to try</param>
    /// <param name="fallbackKeys">Fallback key combinations</param>
    /// <returns>True if any registration successful</returns>
    public bool RegisterHotkeyWithFallback(Keys primaryKey, System.Windows.Input.ModifierKeys primaryModifiers, 
        params (Keys key, System.Windows.Input.ModifierKeys modifiers)[] fallbackKeys)
    {
        // Try primary combination first
        if (RegisterHotkey(primaryKey, primaryModifiers))
            return true;

        // Try fallback combinations
        foreach (var (key, modifiers) in fallbackKeys)
        {
            if (RegisterHotkey(key, modifiers))
                return true;
        }

        return false;
    }

    private void SetupMessageHook()
    {
        if (_windowHandle == IntPtr.Zero)
            return;

        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            // Hotkey message received - this indicates the key combination was pressed
            // We'll handle press/release detection through key state monitoring
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void CheckKeyState(object? sender, EventArgs e)
    {
        if (_disposed || !_isHotkeyRegistered || _isSuppressed)
            return;

        try
        {
            // Check if the registered key combination is currently pressed
            bool isCurrentlyPressed = IsKeyCombinationPressed(_registeredKey, _registeredModifiers);

            if (isCurrentlyPressed && !_isHotkeyPressed)
            {
                // Key just pressed
                _isHotkeyPressed = true;
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }
            else if (!isCurrentlyPressed && _isHotkeyPressed)
            {
                // Key just released
                _isHotkeyPressed = false;
                HotkeyReleased?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception)
        {
            // Ignore errors during key state checking
        }
    }

    private bool IsKeyCombinationPressed(Keys key, System.Windows.Input.ModifierKeys modifiers)
    {
        // Check if the main key is pressed (skip for modifier-only hotkeys)
        if (key != Keys.None && (_nativeMethods.GetAsyncKeyState((int)key) & 0x8000) == 0)
            return false;

        // Check modifier keys
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control) && (_nativeMethods.GetAsyncKeyState(0x11) & 0x8000) == 0) // VK_CONTROL
            return false;

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt) && (_nativeMethods.GetAsyncKeyState(0x12) & 0x8000) == 0) // VK_MENU
            return false;

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift) && (_nativeMethods.GetAsyncKeyState(0x10) & 0x8000) == 0) // VK_SHIFT
            return false;

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows) && 
            (_nativeMethods.GetAsyncKeyState(0x5B) & 0x8000) == 0 && (_nativeMethods.GetAsyncKeyState(0x5C) & 0x8000) == 0) // VK_LWIN, VK_RWIN
            return false;

        return true;
    }

    private uint ConvertModifierKeys(System.Windows.Input.ModifierKeys modifiers)
    {
        uint result = 0;

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))
            result |= MOD_ALT;

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            result |= MOD_CONTROL;

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
            result |= MOD_SHIFT;

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows))
            result |= MOD_WIN;

        return result;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Stop key state monitoring
            _keyStateTimer?.Stop();
            _keyStateTimer = null;

            // Unregister hotkey
            UnregisterHotkey();

            // Remove message hook
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;

            _disposed = true;
        }
    }

    internal void SetWindowHandleForTesting(IntPtr handle)
    {
        _windowHandle = handle;
        SetupMessageHook();
    }

    internal void TriggerKeyStateCheck()
    {
        CheckKeyState(null, EventArgs.Empty);
    }
}

/// <summary>
/// Event arguments for hotkey registration failures
/// </summary>
public class HotkeyRegistrationFailedEventArgs : EventArgs
{
    public string ErrorMessage { get; }
    public Keys AttemptedKey { get; }
    public System.Windows.Input.ModifierKeys AttemptedModifiers { get; }

    public HotkeyRegistrationFailedEventArgs(string errorMessage, Keys attemptedKey, System.Windows.Input.ModifierKeys attemptedModifiers)
    {
        ErrorMessage = errorMessage;
        AttemptedKey = attemptedKey;
        AttemptedModifiers = attemptedModifiers;
    }
}
