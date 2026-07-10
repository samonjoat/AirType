using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AirType.Services;
using AirType.ViewModels;

namespace AirType.Views
{
    public partial class CapsuleWidget : Window
    {
        private ScreenMonitor? _screenMonitor;
        private CapsuleWidgetViewModel? _viewModel;
        private WidgetAnimationManager? _animationManager;
        private IHotkeyManager? _hotkeyManager;
        private Func<Task>? _cancelHandler;
        private readonly IWidgetPositioningService _positioningService;
        private const int DefaultBottomMargin = 10;

        public CapsuleWidget()
        {
            InitializeComponent();
            _positioningService = new WidgetPositioningService();
            InitializeViewModel();
            InitializeWindow();
            InitializeScreenMonitoring();
        }

        private void InitializeViewModel()
        {
            _viewModel = new CapsuleWidgetViewModel();
            DataContext = _viewModel;
            
            // Subscribe to ViewModel events
            _viewModel.DimensionsChanged += OnViewModelDimensionsChanged;
            _viewModel.StateChanged += OnViewModelStateChanged;
        }

        private void InitializeWindow()
        {
            // Ensure window properties are set correctly
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            CancelButton.Focusable = false;
            CancelButton.IsTabStop = false;
            StopButton.Focusable = false;
            StopButton.IsTabStop = false;
            
            // Handle window loaded event
            Loaded += CapsuleWidget_Loaded;
            Closing += CapsuleWidget_Closing;
            
            // Handle mouse events for hover behavior
            MouseEnter += CapsuleWidget_MouseEnter;
            MouseLeave += CapsuleWidget_MouseLeave;
        }

        private void InitializeScreenMonitoring()
        {
            _screenMonitor = new ScreenMonitor();
            _screenMonitor.ActiveScreenChanged += OnActiveScreenChanged;
            
            // Edge Case #11: Monitor display configuration changes (monitor added/removed)
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private void CapsuleWidget_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize animation manager after UI is loaded
            InitializeAnimationManager();
            
            // Initialize waveform renderer with the Path element
            InitializeWaveformRenderer();
            
            // Initialize hotkey manager
            InitializeHotkeyManager();
            
            // Ensure window is positioned correctly after loading
            SetInitialPosition();
            
            // Start monitoring for screen changes
            _screenMonitor?.StartMonitoring();
        }

        private void InitializeAnimationManager()
        {
            _animationManager = new WidgetAnimationManager(
                CapsuleBackground,
                CapsuleContainer,
                CancelButton,
                StopButton,
                WaveformCanvas
            );
            
            _animationManager.AnimationCompleted += OnAnimationCompleted;
            
            // Connect animation manager to view model
            if (_viewModel != null)
            {
                _viewModel.SetAnimationManager(_animationManager);
            }
            
            // Add subtle entrance animation
            PlayEntranceAnimation();
        }

        private void InitializeWaveformRenderer()
        {
            // Connect the waveform Path element to the ViewModel's renderer
            if (_viewModel != null && WaveformPath != null)
            {
                _viewModel.SetWaveformPath(WaveformPath);
            }
        }

        private void InitializeHotkeyManager()
        {
            // HotkeyManager is now provided by ServiceContainer via SetHotkeyManager
        }

        private void RegisterDefaultHotkey()
        {
            // Registration is now handled by App.xaml.cs using AppSettings
        }

        private void NotifyHotkeySelection()
        {
            // Handled by Settings UI events
        }

        private void ScheduleHotkeyRegistration()
        {
            // Handled by App.xaml.cs
        }

        private static string FormatHotkeyDisplay(Keys key, ModifierKeys modifiers)
        {
            var segments = new List<string>();

            if (modifiers.HasFlag(ModifierKeys.Control)) segments.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Shift)) segments.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Alt)) segments.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Windows)) segments.Add("Win");

            segments.Add(key switch
            {
                Keys.Space => "Space",
                _ => key.ToString()
            });

            return string.Join(" + ", segments);
        }

        private static void ShowInfoMessage(string message)
        {
            try
            {
                NotificationDialog.ShowInfo("AirType", message);
            }
            catch
            {
                // Ignore notification dialog failures
            }
        }

        private void PlayEntranceAnimation()
        {
            if (ScaleT == null)
            {
                // Fail gracefully if the transform is unavailable
                return;
            }

            // Animate from a slightly smaller version of the minimal footprint
            const double targetScaleX = 0.4;
            const double targetScaleY = 0.3333;

            ScaleT.SetCurrentValue(System.Windows.Media.ScaleTransform.ScaleXProperty, targetScaleX * 0.8);
            ScaleT.SetCurrentValue(System.Windows.Media.ScaleTransform.ScaleYProperty, targetScaleY * 0.8);
            SetCurrentValue(OpacityProperty, 0.0);

            var scaleEase = new System.Windows.Media.Animation.BackEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                Amplitude = 0.3
            };

            var targetOpacity = _viewModel?.Opacity ?? 0.45;

            // Scale animations (use BeginAnimation so bindings stay intact)
            var scaleXAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = targetScaleX,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = scaleEase
            };

            var scaleYAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = targetScaleY,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = scaleEase
            };

            ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnimation);
            ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnimation);

            // Opacity animation with quadratic easing for a subtle fade-in.
            // FillBehavior.Stop releases OpacityProperty back to its binding after
            // the fade-in completes, so subsequent state-driven opacity changes
            // (minimal -> recording / transcribing) propagate through the binding.
            var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(250),
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };

            BeginAnimation(OpacityProperty, opacityAnimation);
        }

        private void OnAnimationCompleted(object? sender, AnimationCompletedEventArgs e)
        {
            // Handle any post-animation logic if needed
            // Most state management is handled by the ViewModel
        }

        private void CapsuleWidget_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Edge Case #11: Unsubscribe from display settings changes
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                
                // Clean up resources in proper order
                _animationManager?.Dispose();
                _screenMonitor?.Dispose();
                _hotkeyManager?.Dispose();
                _viewModel?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during CapsuleWidget cleanup: {ex.Message}");
            }
        }

        private void OnActiveScreenChanged(object? sender, ScreenChangedEventArgs e)
        {
            // Automatically reposition widget when active screen changes
            RepositionToActiveScreen();
        }

        /// <summary>
        /// Edge Case #11: Handle display configuration changes (monitor added/removed)
        /// </summary>
        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[Edge Case #11] Display settings changed - validating widget position");
            
            // Dispatch to UI thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Check if current position is still valid
                    if (!IsPositionOnAnyScreen())
                    {
                        System.Diagnostics.Debug.WriteLine("[Edge Case #11] Widget is off-screen - repositioning to primary monitor");
                        RepositionToPrimaryScreen();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[Edge Case #11] Widget position is valid");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Edge Case #11] Error handling display change: {ex.Message}");
                }
            }));
        }

        private void CapsuleWidget_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Add enhanced hover animation
            _animationManager?.AnimateHoverEnter();
            
            // Delegate to ViewModel
            _viewModel?.OnMouseEnter();
        }

        private void CapsuleWidget_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Add enhanced hover exit animation
            _animationManager?.AnimateHoverExit();
            
            // Delegate to ViewModel
            _viewModel?.OnMouseLeave();
        }

        private void SetInitialPosition()
        {
            // Edge Case #11: Validate position on startup
            // This handles cases where the widget was saved off-screen (e.g., monitor was disconnected)
            if (!IsPositionOnAnyScreen())
            {
                System.Diagnostics.Debug.WriteLine("[Edge Case #11] Widget position invalid on startup - repositioning");
                RepositionToPrimaryScreen();
            }
            else
            {
                RepositionToActiveScreen();
            }
        }

        private void RepositionToActiveScreen()
        {
            try
            {
                var activeScreen = _screenMonitor?.GetActiveScreen() ?? Screen.PrimaryScreen ?? throw new InvalidOperationException("No screens available");
                var dpiScale = GetDpiScale();
                
                var configuredMargin = _viewModel?.StateManager.Configuration.BottomMargin;
                var bottomMargin = Math.Max(DefaultBottomMargin, configuredMargin ?? DefaultBottomMargin);

                var position = _positioningService.CalculateCenterBottomPosition(
                    activeScreen, Width, Height, bottomMargin, dpiScale);

                if (!position.IsValid)
                {
                    throw new InvalidOperationException("Invalid position calculated");
                }

                var targetPoint = ConvertDevicePointToDips(new System.Windows.Point(position.Left, position.Top), dpiScale);
                
                // Validate calculated position
                if (double.IsNaN(targetPoint.X) || double.IsNaN(targetPoint.Y) ||
                    double.IsInfinity(targetPoint.X) || double.IsInfinity(targetPoint.Y))
                {
                    throw new InvalidOperationException("Invalid position calculated");
                }

                // Ensure position is within reasonable bounds
                var screenBounds = activeScreen.Bounds;
                targetPoint.X = Math.Max(screenBounds.Left - Width, Math.Min(targetPoint.X, screenBounds.Right));
                targetPoint.Y = Math.Max(screenBounds.Top - Height, Math.Min(targetPoint.Y, screenBounds.Bottom));

                Left = targetPoint.X;
                Top = targetPoint.Y;
            }
            catch (Exception)
            {
                // Fallback to primary screen
                RepositionToPrimaryScreen();
            }
        }

        /// <summary>
        /// Edge Case #11: Check if the widget's current position is on any available screen
        /// </summary>
        private bool IsPositionOnAnyScreen()
        {
            return _positioningService.IsPositionOnAnyScreen(Left, Top, Width, Height);
        }

        /// <summary>
        /// Edge Case #11: Reposition widget to primary screen
        /// </summary>
        private void RepositionToPrimaryScreen()
        {
            try
            {
                var position = _positioningService.GetPrimaryScreenFallbackPosition(Width, Height, DefaultBottomMargin);
                
                if (position.IsValid)
                {
                    var dpiScale = GetDpiScale();
                    var targetPoint = ConvertDevicePointToDips(new System.Windows.Point(position.Left, position.Top), dpiScale);
                    
                    if (!double.IsNaN(targetPoint.X) && !double.IsNaN(targetPoint.Y))
                    {
                        Left = targetPoint.X;
                        Top = targetPoint.Y;
                        System.Diagnostics.Debug.WriteLine($"[Edge Case #11] Repositioned to primary screen: ({Left:F0}, {Top:F0})");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Edge Case #11] Error repositioning to primary screen: {ex.Message}");
            }
            
            // Fallback to safe default
            var safePosition = _positioningService.GetSafeDefaultPosition();
            Left = safePosition.Left;
            Top = safePosition.Top;
        }

        /// <summary>
        /// Ensures the widget performs its initial positioning after DPI information is available.
        /// </summary>
        public Task EnsureDpiAwareStartupPositionAsync()
        {
            var completionSource = new TaskCompletionSource<object?>();

            void PositionWidget()
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Validate positioning system before applying
                        if (ValidatePositioningSystem())
                        {
                            RepositionToActiveScreen();
                            
                            // Verify the position was set correctly
                            if (ValidateCurrentPosition())
                            {
                                completionSource.TrySetResult(null);
                            }
                            else
                            {
                                completionSource.TrySetException(new InvalidOperationException("Position validation failed after repositioning"));
                            }
                        }
                        else
                        {
                            completionSource.TrySetException(new InvalidOperationException("Positioning system validation failed"));
                        }
                    }
                    catch (Exception ex)
                    {
                        completionSource.TrySetException(ex);
                    }
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }

            if (IsLoaded && PresentationSource.FromVisual(this) != null)
            {
                PositionWidget();
            }
            else
            {
                RoutedEventHandler? loadedHandler = null;
                loadedHandler = (_, _) =>
                {
                    Loaded -= loadedHandler;
                    PositionWidget();
                };

                Loaded += loadedHandler;
            }

            return completionSource.Task;
        }

        /// <summary>
        /// Validates the positioning system components are working correctly
        /// </summary>
        private bool ValidatePositioningSystem()
        {
            return _positioningService.ValidatePositioningSystem(this, _screenMonitor);
        }

        /// <summary>
        /// Validates that the current position is reasonable
        /// </summary>
        private bool ValidateCurrentPosition()
        {
            return _positioningService.ValidatePosition(Left, Top, Width, Height);
        }

        private DpiScale GetDpiScale()
        {
            return _positioningService.GetDpiScale(this);
        }

        private System.Windows.Point ConvertDevicePointToDips(System.Windows.Point devicePoint, DpiScale dpiScale)
        {
            return _positioningService.ConvertDevicePointToDips(devicePoint, dpiScale, this);
        }

        private void OnViewModelDimensionsChanged(object? sender, WidgetDimensionsChangedEventArgs e)
        {
            // Enhanced bottom anchor positioning with drift prevention
            // Only reposition if not currently animating to avoid conflicts
            if (_animationManager?.IsAnimating != true)
            {
                // Store current bottom position before repositioning
                var currentBottom = Top + Height;
                
                // Use a small delay to allow animation to start first
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_animationManager?.IsAnimating != true)
                    {
                        // Reposition while maintaining bottom anchor
                        RepositionToActiveScreenWithBottomAnchor(currentBottom);
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Repositions the widget while maintaining a specific bottom anchor point
        /// </summary>
        private void RepositionToActiveScreenWithBottomAnchor(double targetBottom)
        {
            try
            {
                var activeScreen = _screenMonitor?.GetActiveScreen() ?? Screen.PrimaryScreen ?? throw new InvalidOperationException("No screens available");
                var dpiScale = GetDpiScale();

                // Calculate horizontal center position using the service
                var centerPosition = _positioningService.CalculateCenterBottomPosition(
                    activeScreen, Width, Height, 0, dpiScale);

                if (centerPosition.IsValid)
                {
                    var centerPoint = ConvertDevicePointToDips(new System.Windows.Point(centerPosition.Left, 0), dpiScale);
                    
                    // Set position maintaining bottom anchor
                    Left = centerPoint.X;
                    Top = targetBottom - Height;
                    
                    // Validate final position
                    if (ValidateCurrentPosition())
                    {
                        return;
                    }
                }
                
                // Fallback to standard repositioning
                RepositionToActiveScreen();
            }
            catch
            {
                // Fallback to standard repositioning
                RepositionToActiveScreen();
            }
        }

        private void OnViewModelStateChanged(object? sender, WidgetStateChangedEventArgs e)
        {
            // Handle any state-specific logic if needed
            // Most UI updates are handled through data binding
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Update renderer with actual canvas size
            if (_viewModel != null && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                _viewModel.UpdateWaveformCanvasSize(e.NewSize.Width, e.NewSize.Height);
            }
        }

        private void WaveformCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Add visual feedback animation
            _animationManager?.AnimateScaleFeedback();
            
            // Delegate to ViewModel
            _viewModel?.OnWidgetClick();

            e.Handled = true;
        }

        private void WaveformCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Handle "unclick" - stop recording if currently recording
            _viewModel?.OnWidgetUnclick();

            e.Handled = true;
        }

        private void CapsuleBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Button)
                return; // Let button handlers run

            _animationManager?.AnimateScaleFeedback();
            _viewModel?.OnWidgetClick();

            e.Handled = true;
        }

        private void CapsuleBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Button)
                return;

            _viewModel?.OnWidgetUnclick();

            e.Handled = true;
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (_cancelHandler != null)
            {
                await _cancelHandler();
                return;
            }

            _viewModel?.OnCancelClick();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // Delegate to ViewModel
            _viewModel?.OnStopClick();
        }

        /// <summary>
        /// Public method to start hotkey recording (called from external components)
        /// </summary>
        public void StartHotkeyRecording()
        {
            _viewModel?.StartHotkeyRecording();
        }

        /// <summary>
        /// Public method to stop hotkey recording (called from external components)
        /// </summary>
        public void StopHotkeyRecording()
        {
            _viewModel?.StopHotkeyRecording();
        }

        /// <summary>
        /// Toggle unattended recording (click to start, click to stop).
        /// Used by Notes page mic button. Bypasses hover state requirement.
        /// </summary>
        public void ToggleUnattendedRecording()
        {
            _viewModel?.ToggleUnattendedRecordingExternal();
        }

        public void SetCancelHandler(Func<Task> cancelHandler)
        {
            _cancelHandler = cancelHandler;
        }

        public Task CancelActiveRecordingAsync()
        {
            return _cancelHandler != null
                ? _cancelHandler()
                : (_viewModel?.CancelRecordingAsync() ?? Task.CompletedTask);
        }

        /// <summary>
        /// Get the current widget state manager
        /// </summary>
        public WidgetStateManager? GetStateManager()
        {
            return _viewModel?.StateManager;
        }

        /// <summary>
        /// Set the audio input manager for waveform visualization
        /// </summary>
        /// <param name="audioInputManager">Audio input manager instance</param>
        public void SetAudioInputManager(IAudioInputManager audioInputManager)
        {
            _viewModel?.SetAudioInputManager(audioInputManager);
        }

        /// <summary>
        /// Set the hotkey manager for global hotkey support
        /// </summary>
        /// <param name="hotkeyManager">Hotkey manager instance</param>
        public void SetHotkeyManager(IHotkeyManager hotkeyManager)
        {
            // Dispose existing hotkey manager if any (if it's not the shared one)
            if (_hotkeyManager != null && _hotkeyManager != hotkeyManager)
            {
                _hotkeyManager.Dispose();
            }
            
            _hotkeyManager = hotkeyManager;
            _hotkeyManager.Initialize(this);
            
            // Connect to view model
            _viewModel?.SetHotkeyManager(_hotkeyManager);
        }

        /// <summary>
        /// Get the current hotkey manager
        /// </summary>
        public IHotkeyManager? GetHotkeyManager()
        {
            return _hotkeyManager;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Make window click-through when in minimal state
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowClickThrough(hwnd, false);
            ApplyNoActivateStyle(hwnd);

            var source = HwndSource.FromHwnd(hwnd);
            if (source != null)
            {
                source.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeConstants.WM_MOUSEACTIVATE)
            {
                if (System.Windows.Application.Current is App app)
                {
                    app.RecordExternalForegroundWindow();
                }

                handled = true;
                return new IntPtr(NativeConstants.MA_NOACTIVATE);
            }

            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        private void SetWindowClickThrough(IntPtr hwnd, bool clickThrough)
        {
            try
            {
                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if (clickThrough)
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
                }
                else
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
                }
            }
            catch (Exception)
            {
                // Ignore errors in click-through setup
            }
        }

        private void ApplyNoActivateStyle(IntPtr hwnd)
        {
            try
            {
                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((extendedStyle & NativeConstants.WS_EX_NOACTIVATE) == 0)
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | NativeConstants.WS_EX_NOACTIVATE);
                }
            }
            catch (Exception)
            {
                // Ignore errors in style adjustment
            }
        }

        private static class NativeConstants
        {
            public const int WM_MOUSEACTIVATE = 0x0021;
            public const int MA_NOACTIVATE = 3;
            public const int WS_EX_NOACTIVATE = 0x08000000;
        }
    }
}
