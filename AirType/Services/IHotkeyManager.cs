using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;

namespace AirType.Services;

/// <summary>
/// Interface for global hotkey management
/// </summary>
public interface IHotkeyManager : IDisposable
{
    /// <summary>
    /// Event fired when hotkey is pressed down
    /// </summary>
    event EventHandler? HotkeyPressed;

    /// <summary>
    /// Event fired when hotkey is released
    /// </summary>
    event EventHandler? HotkeyReleased;

    /// <summary>
    /// Event fired when hotkey registration fails
    /// </summary>
    event EventHandler<HotkeyRegistrationFailedEventArgs>? HotkeyRegistrationFailed;

    /// <summary>
    /// Event fired when the registered hotkey changes (e.g., successful registration or fallback)
    /// </summary>
    event EventHandler? HotkeyChanged;

    /// <summary>
    /// Whether a hotkey is currently registered
    /// </summary>
    bool IsHotkeyRegistered { get; }

    /// <summary>
    /// Whether the hotkey is currently pressed
    /// </summary>
    bool IsHotkeyPressed { get; }

    /// <summary>
    /// When true, hotkey press/release events are suppressed.
    /// Used to prevent hotkey triggers during active workflow processing.
    /// </summary>
    bool IsSuppressed { get; set; }

    /// <summary>
    /// Currently registered key
    /// </summary>
    Keys RegisteredKey { get; }

    /// <summary>
    /// Currently registered modifier keys
    /// </summary>
    System.Windows.Input.ModifierKeys RegisteredModifiers { get; }

    /// <summary>
    /// Initialize the hotkey manager with a window handle
    /// </summary>
    /// <param name="window">WPF window to receive hotkey messages</param>
    void Initialize(Window window);

    /// <summary>
    /// Register a global hotkey
    /// </summary>
    /// <param name="key">Key to register</param>
    /// <param name="modifiers">Modifier keys</param>
    /// <returns>True if registration successful, false otherwise</returns>
    bool RegisterHotkey(Keys key, System.Windows.Input.ModifierKeys modifiers = System.Windows.Input.ModifierKeys.Control);

    /// <summary>
    /// Unregister the current hotkey
    /// </summary>
    void UnregisterHotkey();

    /// <summary>
    /// Try to register a hotkey with fallback options if the primary fails
    /// </summary>
    /// <param name="primaryKey">Primary key to try</param>
    /// <param name="primaryModifiers">Primary modifiers to try</param>
    /// <param name="fallbackKeys">Fallback key combinations</param>
    /// <returns>True if any registration successful</returns>
    bool RegisterHotkeyWithFallback(Keys primaryKey, System.Windows.Input.ModifierKeys primaryModifiers, 
        params (Keys key, System.Windows.Input.ModifierKeys modifiers)[] fallbackKeys);
}
