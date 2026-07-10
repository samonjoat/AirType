using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;

namespace AirType.Services
{
    public class ScreenMonitor : IDisposable
    {
        // Win32 API imports for screen detection
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const int ScreenCheckIntervalMs = 150;
        private System.Threading.Timer? _screenCheckTimer;
        private Screen _lastActiveScreen;
        private readonly object _lockObject = new object();
        private bool _isDisposed = false;

        public event EventHandler<ScreenChangedEventArgs>? ActiveScreenChanged;

        public ScreenMonitor()
        {
            _lastActiveScreen = ResolvePreferredScreen(
                                     TryGetCursorScreen(),
                                     TryGetForegroundWindowScreen(),
                                     previousScreen: null)
                                 ?? Screen.PrimaryScreen
                                 ?? throw new InvalidOperationException("No screens available");
        }

        public void StartMonitoring()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ScreenMonitor));

            // Poll frequently so the widget closely follows pointer movement
            _screenCheckTimer = new System.Threading.Timer(CheckActiveScreen, null, 0, ScreenCheckIntervalMs);
        }

        public void StopMonitoring()
        {
            _screenCheckTimer?.Dispose();
            _screenCheckTimer = null;
        }

        private void CheckActiveScreen(object? state)
        {
            if (_isDisposed)
                return;

            try
            {
                lock (_lockObject)
                {
                    var cursorScreen = TryGetCursorScreen();
                    var windowScreen = TryGetForegroundWindowScreen();

                    var currentActiveScreen = ResolvePreferredScreen(cursorScreen, windowScreen, _lastActiveScreen)
                                             ?? _lastActiveScreen;
                    
                    // Compare screens by their bounds since Screen.Equals might not work as expected
                    if (!AreScreensEqual(currentActiveScreen, _lastActiveScreen))
                    {
                        var previousScreen = _lastActiveScreen;
                        _lastActiveScreen = currentActiveScreen;
                        
                        // Raise event on UI thread
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                            DispatcherPriority.Normal,
                            new Action(() =>
                            {
                                ActiveScreenChanged?.Invoke(this, 
                                    new ScreenChangedEventArgs(currentActiveScreen, previousScreen));
                            })
                        );
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle exceptions to prevent timer from stopping
                // In a production app, you might want to log this
            }
        }

        public Screen GetActiveScreen()
        {
            var cursorScreen = TryGetCursorScreen();
            var windowScreen = TryGetForegroundWindowScreen();

            return ResolvePreferredScreen(cursorScreen, windowScreen, _lastActiveScreen)
                   ?? _lastActiveScreen
                   ?? Screen.PrimaryScreen
                   ?? throw new InvalidOperationException("No screens available");
        }

        public static ScreenPosition CalculateCenterBottomPosition(Screen screen, double widgetWidth, double widgetHeight, int bottomMargin = 40)
        {
            if (screen == null)
                throw new ArgumentNullException(nameof(screen));

            // Validate input parameters
            if (widgetWidth <= 0 || widgetHeight <= 0 || 
                double.IsNaN(widgetWidth) || double.IsNaN(widgetHeight) ||
                double.IsInfinity(widgetWidth) || double.IsInfinity(widgetHeight))
            {
                throw new ArgumentException("Widget dimensions must be positive finite values");
            }

            var workingArea = screen.WorkingArea;
            
            // Validate screen working area
            if (workingArea.Width <= 0 || workingArea.Height <= 0)
            {
                throw new InvalidOperationException("Screen working area has invalid dimensions");
            }
            
            // Calculate center-bottom position with enhanced validation
            double left = workingArea.Left + (workingArea.Width - widgetWidth) / 2.0;
            double top = workingArea.Bottom - widgetHeight - bottomMargin;

            // Validate calculated position
            if (double.IsNaN(left) || double.IsNaN(top) ||
                double.IsInfinity(left) || double.IsInfinity(top))
            {
                throw new InvalidOperationException("Position calculation resulted in invalid values");
            }

            // Ensure the widget stays within screen bounds with enhanced clamping
            var minLeft = workingArea.Left - (widgetWidth * 0.5); // Allow 50% off-screen horizontally
            var maxLeft = workingArea.Right - (widgetWidth * 0.5); // Allow 50% off-screen horizontally
            var minTop = workingArea.Top;
            var maxTop = workingArea.Bottom - Math.Min(widgetHeight, 10); // Ensure at least 10px visible

            left = Math.Max(minLeft, Math.Min(left, maxLeft));
            top = Math.Max(minTop, Math.Min(top, maxTop));

            // Final validation
            if (double.IsNaN(left) || double.IsNaN(top) ||
                double.IsInfinity(left) || double.IsInfinity(top))
            {
                // Fallback to safe center position
                left = workingArea.Left + workingArea.Width / 2.0 - widgetWidth / 2.0;
                top = workingArea.Bottom - widgetHeight - 10;
            }

            return new ScreenPosition
            {
                Left = left,
                Top = top,
                Screen = screen
            };
        }

        private static bool AreScreensEqual(Screen? screen1, Screen? screen2)
        {
            if (screen1 == null && screen2 == null)
                return true;
            
            if (screen1 == null || screen2 == null)
                return false;

            return screen1.Bounds.Equals(screen2.Bounds) && 
                   screen1.WorkingArea.Equals(screen2.WorkingArea);
        }

        private static Screen? TryGetCursorScreen()
        {
            try
            {
                if (GetCursorPos(out var point))
                {
                    return Screen.FromPoint(new System.Drawing.Point(point.X, point.Y));
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        private static Screen? TryGetForegroundWindowScreen()
        {
            try
            {
                IntPtr activeWindow = GetForegroundWindow();
                if (activeWindow != IntPtr.Zero)
                {
                    return Screen.FromHandle(activeWindow);
                }
            }
            catch
            {
                // Ignore and fallback
            }

            return null;
        }

        private Screen? ResolvePreferredScreen(Screen? cursorScreen, Screen? foregroundScreen, Screen? previousScreen)
        {
            if (cursorScreen != null)
            {
                return cursorScreen;
            }

            if (foregroundScreen != null)
            {
                return foregroundScreen;
            }

            return previousScreen;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                StopMonitoring();
                _isDisposed = true;
            }
        }
    }

    public class ScreenChangedEventArgs : EventArgs
    {
        public Screen NewScreen { get; }
        public Screen PreviousScreen { get; }

        public ScreenChangedEventArgs(Screen newScreen, Screen previousScreen)
        {
            NewScreen = newScreen;
            PreviousScreen = previousScreen;
        }
    }

    public class ScreenPosition
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public Screen Screen { get; set; } = null!;
    }
}
