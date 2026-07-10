using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AirType.Services;
using AirType.Views;
using MaterialDesignThemes.Wpf;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinForms = System.Windows.Forms;

namespace AirType;

public partial class MainWindow : Window
{
    private readonly ThemeService _themeService;
    private bool _isDarkTheme = false;
    private bool _isSidebarCollapsed = false;
    private Button? _currentActiveButton = null;
    private AppWindow? _appWindow;
    private bool _isAppWindowTitleBarEnabled = false;
    private bool _restoreMaximizedOnSourceInitialized = false;
    private WindowState _lastNonMinimizedWindowState = WindowState.Normal;

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double DefaultWindowWidth = 840;
    private const double DefaultWindowHeight = 650;
    private const double MinimumWindowWidth = 840;
    private const double MinimumWindowHeight = 650;
    
    public MainWindow()
    {
        InitializeComponent();
        _themeService = new ThemeService();
        
        // Load saved settings (must be before showing window)
        LoadSettings();
        
        // Set initial page based on saved state or default to History
        NavigateToLastPage();
        
        // Save settings when window closes
        this.Closing += MainWindow_Closing;
        
        // Track window state for restore and persistence.
        this.StateChanged += MainWindow_StateChanged;
        this.SourceInitialized += MainWindow_SourceInitialized;
        this.DpiChanged += MainWindow_DpiChanged;
    }
    
    #region Window Controls

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowMessageHook);
            InitializeAppWindowTitleBar(source.Handle);
            ApplyWindowPlacementLimits(source.Handle);
        }
    }

    private void InitializeAppWindowTitleBar(IntPtr hwnd)
    {
        try
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            if (_appWindow is null || !AppWindowTitleBar.IsCustomizationSupported())
            {
                UseDefaultSystemTitleBar();
                return;
            }

            _appWindow.Title = Title;
            _appWindow.Changed += AppWindow_Changed;

            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

            _isAppWindowTitleBarEnabled = true;
            AppTitleBar.Visibility = Visibility.Visible;
            TitleBarRow.Height = new GridLength(48);
            UpdateAppWindowTitleBarLayout();
            UpdateAppWindowTitleBarColors();
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to initialize AppWindow title bar; falling back to the default system title bar.", ex);
            UseDefaultSystemTitleBar();
        }
    }

    private void UseDefaultSystemTitleBar()
    {
        _isAppWindowTitleBarEnabled = false;
        AppTitleBar.Visibility = Visibility.Collapsed;
        TitleBarRow.Height = new GridLength(0);

        try
        {
            _appWindow?.TitleBar.ResetToDefault();
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to reset AppWindow title bar to the system default.", ex);
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_isAppWindowTitleBarEnabled)
        {
            return;
        }

        if (args.DidSizeChange || args.DidPresenterChange)
        {
            Dispatcher.Invoke(UpdateAppWindowTitleBarLayout);
        }
    }

    private void UpdateAppWindowTitleBarLayout()
    {
        if (!_isAppWindowTitleBarEnabled || _appWindow is null)
        {
            return;
        }

        var scale = GetCurrentDpiScale();
        LeftCaptionPaddingColumn.Width = new GridLength(_appWindow.TitleBar.LeftInset / scale.X);
        RightCaptionPaddingColumn.Width = new GridLength(_appWindow.TitleBar.RightInset / scale.X);
    }

    private void UpdateAppWindowTitleBarColors()
    {
        if (!_isAppWindowTitleBarEnabled || _appWindow is null)
        {
            return;
        }

        var titleBar = _appWindow.TitleBar;
        titleBar.BackgroundColor = GetResourceColor("BgBrush", Microsoft.UI.Colors.Transparent);
        titleBar.ForegroundColor = GetResourceColor("TextPrimaryBrush", Microsoft.UI.Colors.Black);
        titleBar.InactiveBackgroundColor = GetResourceColor("BgBrush", Microsoft.UI.Colors.Transparent);
        titleBar.InactiveForegroundColor = GetResourceColor("TextMutedBrush", Microsoft.UI.Colors.Gray);
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = GetResourceColor("BgMutedBrush", Microsoft.UI.Colors.Transparent);
        titleBar.ButtonPressedBackgroundColor = GetResourceColor("BorderBrush", Microsoft.UI.Colors.Transparent);
        titleBar.ButtonForegroundColor = GetResourceColor("TextSecondaryBrush", Microsoft.UI.Colors.Black);
        titleBar.ButtonInactiveForegroundColor = GetResourceColor("TextMutedBrush", Microsoft.UI.Colors.Gray);
        titleBar.ButtonHoverForegroundColor = GetResourceColor("TextPrimaryBrush", Microsoft.UI.Colors.Black);
        titleBar.ButtonPressedForegroundColor = GetResourceColor("TextPrimaryBrush", Microsoft.UI.Colors.Black);
    }

    private global::Windows.UI.Color GetResourceColor(string resourceKey, global::Windows.UI.Color fallback)
    {
        if (TryFindResource(resourceKey) is SolidColorBrush brush)
        {
            return global::Windows.UI.Color.FromArgb(
                brush.Color.A,
                brush.Color.R,
                brush.Color.G,
                brush.Color.B);
        }

        return fallback;
    }

    private DpiScaleInfo GetCurrentDpiScale()
    {
        if (PresentationSource.FromVisual(this) is HwndSource source && source.Handle != IntPtr.Zero)
        {
            return GetWindowDpiScale(source.Handle);
        }

        return new DpiScaleInfo(1.0, 1.0);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            ApplyWindowSizingLimits(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ApplyWindowSizingLimits(IntPtr hwnd, IntPtr lParam)
    {
        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        int? workAreaWidth = null;
        int? workAreaHeight = null;

        if (TryGetMonitorBounds(hwnd, out var workArea, out _))
        {
            workAreaWidth = workArea.Right - workArea.Left;
            workAreaHeight = workArea.Bottom - workArea.Top;
        }

        var dpiScale = GetWindowDpiScale(hwnd);
        var minTrackSize = CalculateMinimumTrackSize(
            minMaxInfo.MinTrackSize.X,
            minMaxInfo.MinTrackSize.Y,
            MinWidth,
            MinHeight,
            dpiScale.X,
            dpiScale.Y,
            workAreaWidth,
            workAreaHeight);

        minMaxInfo.MinTrackSize.X = minTrackSize.Width;
        minMaxInfo.MinTrackSize.Y = minTrackSize.Height;

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    internal static (int Width, int Height) CalculateMinimumTrackSize(
        int currentMinTrackWidth,
        int currentMinTrackHeight,
        double minWidthDips,
        double minHeightDips,
        double dpiScaleX,
        double dpiScaleY,
        int? workAreaWidth,
        int? workAreaHeight)
    {
        var minTrackWidth = DipsToPhysicalPixels(minWidthDips, dpiScaleX);
        var minTrackHeight = DipsToPhysicalPixels(minHeightDips, dpiScaleY);

        if (workAreaWidth is { } maxWidth)
        {
            minTrackWidth = Math.Min(minTrackWidth, maxWidth);
        }

        if (workAreaHeight is { } maxHeight)
        {
            minTrackHeight = Math.Min(minTrackHeight, maxHeight);
        }

        return (
            Math.Max(currentMinTrackWidth, minTrackWidth),
            Math.Max(currentMinTrackHeight, minTrackHeight));
    }

    private void ApplyWindowPlacementLimits(IntPtr hwnd)
    {
        if (!TryGetSavedMonitorBounds(out var workArea, out _) &&
            !TryGetMonitorBounds(hwnd, out workArea, out _))
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var dpiScale = GetWindowDpiScale(hwnd);
        var workLeft = PhysicalPixelsToDips(workArea.Left, dpiScale.X);
        var workTop = PhysicalPixelsToDips(workArea.Top, dpiScale.Y);
        var workWidth = PhysicalPixelsToDips(workArea.Right - workArea.Left, dpiScale.X);
        var workHeight = PhysicalPixelsToDips(workArea.Bottom - workArea.Top, dpiScale.Y);

        MinWidth = Math.Min(MinimumWindowWidth, Math.Floor(workWidth));
        MinHeight = Math.Min(MinimumWindowHeight, Math.Floor(workHeight));
        Width = Math.Min(ClampSavedDimension(Width, DefaultWindowWidth, MinimumWindowWidth), Math.Floor(workWidth));
        Height = Math.Min(ClampSavedDimension(Height, DefaultWindowHeight, MinimumWindowHeight), Math.Floor(workHeight));

        var maxLeft = workLeft + Math.Max(0, workWidth - Width);
        var maxTop = workTop + Math.Max(0, workHeight - Height);

        Left = ClampToRange(Left, workLeft, maxLeft);
        Top = ClampToRange(Top, workTop, maxTop);

        if (_restoreMaximizedOnSourceInitialized)
        {
            WindowState = WindowState.Maximized;
            _lastNonMinimizedWindowState = WindowState.Maximized;
            _restoreMaximizedOnSourceInitialized = false;
        }
    }

    private static bool TryGetMonitorBounds(IntPtr hwnd, out RectInfo workArea, out RectInfo monitorArea)
    {
        workArea = default;
        monitorArea = default;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        workArea = monitorInfo.WorkArea;
        monitorArea = monitorInfo.MonitorArea;
        return true;
    }

    private static bool TryGetSavedMonitorBounds(out RectInfo workArea, out RectInfo monitorArea)
    {
        workArea = default;
        monitorArea = default;

        var savedMonitor = Properties.Settings.Default.WindowMonitorDeviceName;
        if (string.IsNullOrWhiteSpace(savedMonitor))
        {
            return false;
        }

        foreach (var screen in WinForms.Screen.AllScreens)
        {
            if (!string.Equals(screen.DeviceName, savedMonitor, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            workArea = ToRectInfo(screen.WorkingArea);
            monitorArea = ToRectInfo(screen.Bounds);
            return true;
        }

        return false;
    }

    private static RectInfo ToRectInfo(System.Drawing.Rectangle rectangle)
    {
        return new RectInfo
        {
            Left = rectangle.Left,
            Top = rectangle.Top,
            Right = rectangle.Right,
            Bottom = rectangle.Bottom
        };
    }

    private DpiScaleInfo GetWindowDpiScale(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            if (dpi > 0)
            {
                var scale = dpi / 96.0;
                return new DpiScaleInfo(scale, scale);
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows versions do not expose GetDpiForWindow.
        }

        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            return new DpiScaleInfo(target.TransformToDevice.M11, target.TransformToDevice.M22);
        }

        return new DpiScaleInfo(1.0, 1.0);
    }

    private static int DipsToPhysicalPixels(double dips, double scale)
    {
        return (int)Math.Ceiling(dips * scale);
    }

    private static double PhysicalPixelsToDips(double pixels, double scale)
    {
        return pixels / scale;
    }

    private static double ClampToRange(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }
    
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            _lastNonMinimizedWindowState = WindowState;
        }
    }

    private void MainWindow_DpiChanged(object sender, DpiChangedEventArgs e)
    {
        UpdateAppWindowTitleBarLayout();
    }

    public void RestoreForActivation()
    {
        if (ContentArea.Content == null)
        {
            NavigateToLastPage();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = _lastNonMinimizedWindowState == WindowState.Maximized
                ? WindowState.Maximized
                : WindowState.Normal;
        }

        Show();
        Activate();
        Focus();

        Topmost = true;
        Topmost = false;
    }
    #endregion

    [StructLayout(LayoutKind.Sequential)]
    private struct PointInfo
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointInfo Reserved;
        public PointInfo MaxSize;
        public PointInfo MaxPosition;
        public PointInfo MinTrackSize;
        public PointInfo MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectInfo
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public RectInfo MonitorArea;
        public RectInfo WorkArea;
        public uint Flags;
    }

    private readonly struct DpiScaleInfo
    {
        public DpiScaleInfo(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RectInfo rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    
    private void LoadSettings()
    {
        try
        {
            // Load theme preference
            _isDarkTheme = Properties.Settings.Default.IsDarkTheme;
            _themeService.SetTheme(_isDarkTheme);
            ThemeIcon.Kind = _isDarkTheme ? PackIconKind.WeatherNight : PackIconKind.WhiteBalanceSunny;
            
            // Load sidebar state
            _isSidebarCollapsed = Properties.Settings.Default.IsSidebarCollapsed;
            if (_isSidebarCollapsed)
            {
                // Apply collapsed state without animation
                ApplySidebarCollapsedState();
            }
            
            // Load window state
            var savedWidth = ClampSavedDimension(
                Properties.Settings.Default.WindowWidth,
                DefaultWindowWidth,
                MinimumWindowWidth);
            var savedHeight = ClampSavedDimension(
                Properties.Settings.Default.WindowHeight,
                DefaultWindowHeight,
                MinimumWindowHeight);
            var savedLeft = Properties.Settings.Default.WindowLeft;
            var savedTop = Properties.Settings.Default.WindowTop;
            _restoreMaximizedOnSourceInitialized = Properties.Settings.Default.WindowMaximized;
            _lastNonMinimizedWindowState = _restoreMaximizedOnSourceInitialized
                ? WindowState.Maximized
                : WindowState.Normal;
            
            // CRITICAL: Set to Manual so WPF respects our position
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            
            // Apply window size; SourceInitialized clamps it to the active monitor work area.
            this.Width = savedWidth;
            this.Height = savedHeight;
            
            // Apply window position
            this.Left = savedLeft;
            this.Top = savedTop;
        }
        catch
        {
            // Use defaults if settings fail to load
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private static double ClampSavedDimension(double value, double fallback, double minimum)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum)
        {
            return fallback;
        }

        return value;
    }
    
    private void SaveSettings()
    {
        try
        {
            Properties.Settings.Default.IsDarkTheme = _isDarkTheme;
            Properties.Settings.Default.IsSidebarCollapsed = _isSidebarCollapsed;
            
            var stateToPersist = this.WindowState == WindowState.Minimized
                ? _lastNonMinimizedWindowState
                : this.WindowState;

            Properties.Settings.Default.WindowMaximized = (stateToPersist == WindowState.Maximized);
            Properties.Settings.Default.WindowMonitorDeviceName = GetCurrentMonitorDeviceName();
            
            // Save normal bounds. If maximized, RestoreBounds contains the restore target.
            if (stateToPersist == WindowState.Normal && this.WindowState != WindowState.Minimized)
            {
                if (TryGetCurrentWindowBoundsInDips(out var left, out var top, out var width, out var height))
                {
                    Properties.Settings.Default.WindowWidth = width;
                    Properties.Settings.Default.WindowHeight = height;
                    Properties.Settings.Default.WindowLeft = left;
                    Properties.Settings.Default.WindowTop = top;
                }
                else
                {
                    Properties.Settings.Default.WindowWidth = this.Width;
                    Properties.Settings.Default.WindowHeight = this.Height;
                    Properties.Settings.Default.WindowLeft = this.Left;
                    Properties.Settings.Default.WindowTop = this.Top;
                }
            }
            else if (stateToPersist == WindowState.Maximized)
            {
                Properties.Settings.Default.WindowWidth = this.RestoreBounds.Width;
                Properties.Settings.Default.WindowHeight = this.RestoreBounds.Height;
                Properties.Settings.Default.WindowLeft = this.RestoreBounds.Left;
                Properties.Settings.Default.WindowTop = this.RestoreBounds.Top;
            }
            
            // Save last active page
            if (_currentActiveButton == HistoryButton)
                Properties.Settings.Default.LastActivePage = "History";
            else if (_currentActiveButton == NotesButton)
                Properties.Settings.Default.LastActivePage = "Notes";
            else if (_currentActiveButton == DictionaryButton)
                Properties.Settings.Default.LastActivePage = "Dictionary";
            else if (_currentActiveButton == StatisticsButton)
                Properties.Settings.Default.LastActivePage = "Statistics";
            else if (_currentActiveButton == SettingsButton)
                Properties.Settings.Default.LastActivePage = "Settings";
            
            Properties.Settings.Default.Save();
        }
        catch
        {
            // Ignore save errors
        }
    }

    private bool TryGetCurrentWindowBoundsInDips(
        out double left,
        out double top,
        out double width,
        out double height)
    {
        left = top = width = height = 0;

        if (PresentationSource.FromVisual(this) is not HwndSource source ||
            source.Handle == IntPtr.Zero ||
            !GetWindowRect(source.Handle, out var rect))
        {
            return false;
        }

        var dpiScale = GetWindowDpiScale(source.Handle);
        left = PhysicalPixelsToDips(rect.Left, dpiScale.X);
        top = PhysicalPixelsToDips(rect.Top, dpiScale.Y);
        width = PhysicalPixelsToDips(rect.Right - rect.Left, dpiScale.X);
        height = PhysicalPixelsToDips(rect.Bottom - rect.Top, dpiScale.Y);
        return true;
    }

    private string GetCurrentMonitorDeviceName()
    {
        try
        {
            if (PresentationSource.FromVisual(this) is HwndSource source &&
                source.Handle != IntPtr.Zero)
            {
                return WinForms.Screen.FromHandle(source.Handle).DeviceName;
            }
        }
        catch
        {
            // Keep monitor persistence best-effort.
        }

        return Properties.Settings.Default.WindowMonitorDeviceName ?? string.Empty;
    }
    
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();

        var app = (App)Application.Current;

        if (!app.IsExplicitShutdownRequested && app.Services?.AppSettingsManager?.MinimizeToTray == true)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        DisposeCurrentContent();
    }
    
    private void NavigateToLastPage()
    {
        try
        {
            var lastPage = Properties.Settings.Default.LastActivePage;

            switch (lastPage)
            {
                case "Transcribe":
                case "History":
                    NavigateToHistory(null!, null!);
                    break;
                case "Notes":
                    NavigateToNotes(null!, null!);
                    break;
                case "Dictionary":
                    NavigateToDictionary(null!, null!);
                    break;
                case "Statistics":
                    NavigateToStatistics(null!, null!);
                    break;
                case "Settings":
                    NavigateToSettings(null!, null!);
                    break;
                default:
                    NavigateToHistory(null!, null!);
                    break;
            }
        }
        catch
        {
            // Default to History if anything goes wrong
            NavigateToHistory(null!, null!);
        }
    }
    
    private void ApplySidebarCollapsedState()
    {
        SidebarColumn.Width = new GridLength(64);
        CollapseButton.ToolTip = "Expand sidebar";
        CollapseIcon.Kind = PackIconKind.ChevronRight;

        AppLogoPanel.Visibility = Visibility.Collapsed;
        AppTitle.Visibility = Visibility.Collapsed;
        SectionMainHeader.Visibility = Visibility.Collapsed;
        SectionInsightsHeader.Visibility = Visibility.Collapsed;
        SectionSystemHeader.Visibility = Visibility.Collapsed;
        HistoryLabel.Visibility = Visibility.Collapsed;
        NotesLabel.Visibility = Visibility.Collapsed;
        DictionaryLabel.Visibility = Visibility.Collapsed;
        StatisticsLabel.Visibility = Visibility.Collapsed;
        SettingsLabel.Visibility = Visibility.Collapsed;
        ThemeText.Visibility = Visibility.Collapsed;
        CollapseLabel.Visibility = Visibility.Collapsed;

        HistoryIcon.Margin = new Thickness(0);
        NotesIcon.Margin = new Thickness(0);
        DictionaryIcon.Margin = new Thickness(0);
        StatisticsIcon.Margin = new Thickness(0);
        SettingsIcon.Margin = new Thickness(0);
        CollapseIcon.Margin = new Thickness(0);

        // Increase icon size when collapsed (match nav buttons)
        AppIconBorder.Width = 44;
        AppIconBorder.Height = 44;
        AppIconBorder.Margin = new Thickness(0);
        AppLogoPanel.Margin = new Thickness(0);
        AppLogoPanel.HorizontalAlignment = HorizontalAlignment.Center;

        SidebarNav.Margin = new Thickness(13, 16, 13, 16);
        SidebarFooter.Margin = new Thickness(13, 0, 13, 0);

        HistoryButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        NotesButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        DictionaryButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        StatisticsButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        SettingsButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        ThemeToggleBorder.HorizontalContentAlignment = HorizontalAlignment.Center;
        CollapseButton.HorizontalContentAlignment = HorizontalAlignment.Center;

        // Make buttons square when collapsed and remove padding for perfect centering.
        HistoryButton.Width = 38;
        HistoryButton.Height = 38;
        HistoryButton.Padding = new Thickness(0, 0, 0, 0);
        NotesButton.Width = 38;
        NotesButton.Height = 38;
        NotesButton.Padding = new Thickness(0, 0, 0, 0);
        DictionaryButton.Width = 38;
        DictionaryButton.Height = 38;
        DictionaryButton.Padding = new Thickness(0, 0, 0, 0);
        StatisticsButton.Width = 38;
        StatisticsButton.Height = 38;
        StatisticsButton.Padding = new Thickness(0, 0, 0, 0);
        SettingsButton.Width = 38;
        SettingsButton.Height = 38;
        SettingsButton.Padding = new Thickness(0, 0, 0, 0);

        CollapseButton.Width = 38;
        CollapseButton.Height = 38;
        CollapseButton.Padding = new Thickness(0, 0, 0, 0);

        ThemeToggleBorder.Width = 38;
        ThemeToggleBorder.Height = 38;
        ThemeToggleBorder.Padding = new Thickness(6, 6, 6, 6);
        ThemeToggleBorder.HorizontalAlignment = HorizontalAlignment.Center;
        ThemeTextColumn.Width = new GridLength(0);
    }
    
    private void ToggleTheme(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        _themeService.SetTheme(_isDarkTheme);
        
        // Update theme icon
        ThemeIcon.Kind = _isDarkTheme ? PackIconKind.WeatherNight : PackIconKind.WhiteBalanceSunny;
        UpdateAppWindowTitleBarColors();
        
        // Refresh active button with new theme colors
        if (_currentActiveButton != null)
        {
            SetActiveButton(_currentActiveButton);
        }
    }
    
    private void ToggleSidebar(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        
        if (_isSidebarCollapsed)
        {
            ApplySidebarCollapsedState();
        }
        else
        {
            CollapseButton.ToolTip = "Collapse sidebar";
            CollapseIcon.Kind = PackIconKind.ChevronLeft;

            // Expand sidebar - show icons and text
            SidebarColumn.Width = new GridLength(220);

            // Show all text labels
            AppLogoPanel.Visibility = Visibility.Collapsed;
            AppTitle.Visibility = Visibility.Collapsed;
            SectionMainHeader.Visibility = Visibility.Visible;
            SectionInsightsHeader.Visibility = Visibility.Visible;
            SectionSystemHeader.Visibility = Visibility.Visible;
            HistoryLabel.Visibility = Visibility.Visible;
            NotesLabel.Visibility = Visibility.Visible;
            DictionaryLabel.Visibility = Visibility.Visible;
            StatisticsLabel.Visibility = Visibility.Visible;
            SettingsLabel.Visibility = Visibility.Visible;
            ThemeText.Visibility = Visibility.Visible;
            CollapseLabel.Visibility = Visibility.Visible;

            // Restore icon margins for proper spacing with text (ALL nav icons including collapse)
            HistoryIcon.Margin = new Thickness(0, 0, 11, 0);
            NotesIcon.Margin = new Thickness(0, 0, 11, 0);
            DictionaryIcon.Margin = new Thickness(0, 0, 11, 0);
            StatisticsIcon.Margin = new Thickness(0, 0, 11, 0);
            SettingsIcon.Margin = new Thickness(0, 0, 11, 0);
            CollapseIcon.Margin = new Thickness(0, 0, 11, 0);

            // Restore app icon/logo positioning (size stays 44x44)
            AppIconBorder.Margin = new Thickness(0, 0, 11, 0);
            AppLogoPanel.Margin = new Thickness(0);
            AppLogoPanel.HorizontalAlignment = HorizontalAlignment.Left;

            // Restore sidebar padding
            SidebarNav.Margin = new Thickness(14, 16, 14, 16);
            SidebarFooter.Margin = new Thickness(14, 0, 14, 0);

            // Left-align button content
            HistoryButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            NotesButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            DictionaryButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            StatisticsButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            SettingsButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            ThemeToggleBorder.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            CollapseButton.HorizontalContentAlignment = HorizontalAlignment.Left;

            // Reset button sizes and padding to auto when expanded
            HistoryButton.Width = double.NaN;
            HistoryButton.Height = double.NaN;
            HistoryButton.Padding = new Thickness(13, 9, 13, 9);
            NotesButton.Width = double.NaN;
            NotesButton.Height = double.NaN;
            NotesButton.Padding = new Thickness(13, 9, 13, 9);
            DictionaryButton.Width = double.NaN;
            DictionaryButton.Height = double.NaN;
            DictionaryButton.Padding = new Thickness(13, 9, 13, 9);
            StatisticsButton.Width = double.NaN;
            StatisticsButton.Height = double.NaN;
            StatisticsButton.Padding = new Thickness(13, 9, 13, 9);
            SettingsButton.Width = double.NaN;
            SettingsButton.Height = double.NaN;
            SettingsButton.Padding = new Thickness(13, 9, 13, 9);

            // Restore collapse button
            CollapseButton.Width = double.NaN;
            CollapseButton.Height = double.NaN;
            CollapseButton.Padding = new Thickness(11, 9, 11, 9);

            // Restore theme toggle size and padding
            ThemeToggleBorder.Width = double.NaN;
            ThemeToggleBorder.Height = double.NaN;
            ThemeToggleBorder.Padding = new Thickness(10, 5, 10, 5);
            ThemeToggleBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
            ThemeTextColumn.Width = new GridLength(1, GridUnitType.Star);
        }
    }
    
    private (Button Btn, FrameworkElement Icon, TextBlock Label)[] GetNavEntries() => new (Button Btn, FrameworkElement Icon, TextBlock Label)[]
    {
        (HistoryButton,    HistoryIcon,    HistoryLabel),
        (NotesButton,      NotesIcon,      NotesLabel),
        (DictionaryButton, DictionaryIcon, DictionaryLabel),
        (StatisticsButton, StatisticsIcon, StatisticsLabel),
        (SettingsButton,   SettingsIcon,   SettingsLabel),
    };

    private void ClearActiveStates()
    {
        var secondary = (Brush)FindResource("TextSecondaryBrush");
        foreach (var (btn, icon, label) in GetNavEntries())
        {
            btn.Tag = null;
            icon.Tag = secondary;
            label.Foreground = secondary;
        }
    }

    private void SetActiveButton(Button button)
    {
        ClearActiveStates();
        var primary = (Brush)FindResource("TextPrimaryBrush");
        button.Tag = "active";
        foreach (var (btn, icon, label) in GetNavEntries())
        {
            if (btn == button)
            {
                icon.Tag = primary;
                label.Foreground = primary;
                break;
            }
        }

        _currentActiveButton = button;
    }
    
    public void NavigateToHistory(object? sender = null, RoutedEventArgs? e = null)
    {
        NavigateToContent(new HistoryView(), HistoryButton);
    }

    public void NavigateToNotes(object? sender = null, RoutedEventArgs? e = null)
    {
        NavigateToContent(new NotesView(), NotesButton);
    }
    
    public void NavigateToDictionary(object? sender = null, RoutedEventArgs? e = null)
    {
        NavigateToContent(new DictionaryView(), DictionaryButton);
    }
    
    public void NavigateToSettings(object? sender = null, RoutedEventArgs? e = null)
    {
        NavigateToContent(new SettingsView(), SettingsButton);
    }

    public void NavigateToStatistics(object? sender = null, RoutedEventArgs? e = null)
    {
        NavigateToContent(new StatisticsView(), StatisticsButton);
    }

    public void RefreshVisibleHistory()
    {
        if (ContentArea.Content is HistoryView historyView)
        {
            historyView.RefreshNow();
        }
    }

    public void RefreshVisibleSettingsDiagnostics()
    {
        if (ContentArea.Content is SettingsView settingsView)
        {
            settingsView.RefreshDiagnosticsNow();
        }
    }

    private void NavigateToContent(UserControl view, Button button)
    {
        DisposeCurrentContent();
        ContentArea.Content = view;
        SetActiveButton(button);
    }

    private void DisposeCurrentContent()
    {
        if (ContentArea.Content is not FrameworkElement currentContent)
        {
            ContentArea.Content = null;
            return;
        }

        if (currentContent is IDisposable disposableContent)
        {
            disposableContent.Dispose();
        }
        else if (currentContent.DataContext is IDisposable disposableViewModel)
        {
            disposableViewModel.Dispose();
        }

        ContentArea.Content = null;
    }

    private void ContentClipGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Clip = new RectangleGeometry(
                new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 0, 0);
        }
    }
}
