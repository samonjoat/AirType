using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace AirType.Services
{
    /// <summary>
    /// Service for managing application theme (Light/Dark mode).
    /// Handles theme switching, persistence, and runtime resource updates.
    /// </summary>
    public class ThemeService
    {
        private const string ThemeFileName = "theme.txt";
        private const string LightThemeName = "light";
        private const string DarkThemeName = "dark";

        private readonly string _themeFilePath;
        private bool _isDarkTheme;

        /// <summary>
        /// Gets whether the current theme is dark.
        /// </summary>
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            private set
            {
                if (_isDarkTheme != value)
                {
                    _isDarkTheme = value;
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Event raised when the theme changes.
        /// </summary>
        public event EventHandler? ThemeChanged;

        public ThemeService()
        {
            var appDataPath = AirTypeStoragePaths.CanonicalRoot;
            
            Directory.CreateDirectory(appDataPath);
            _themeFilePath = Path.Combine(appDataPath, ThemeFileName);
        }

        /// <summary>
        /// Initializes the theme service and applies the saved theme.
        /// Call this from App.xaml.cs OnStartup.
        /// </summary>
        public void Initialize()
        {
            var savedTheme = LoadThemePreference();
            _isDarkTheme = savedTheme == DarkThemeName;
            ApplyTheme();
            
            Logger.Info("ThemeService", $"Initialized with {(IsDarkTheme ? "dark" : "light")} theme");
        }

        /// <summary>
        /// Toggles between light and dark themes.
        /// </summary>
        public void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            SaveThemePreference(IsDarkTheme ? DarkThemeName : LightThemeName);
            ApplyTheme();
            
            Logger.Info("ThemeService", $"Theme toggled to {(IsDarkTheme ? "dark" : "light")}");
        }

        /// <summary>
        /// Sets a specific theme.
        /// </summary>
        /// <param name="isDark">True for dark theme, false for light theme.</param>
        public void SetTheme(bool isDark)
        {
            if (IsDarkTheme == isDark)
                return;

            IsDarkTheme = isDark;
            SaveThemePreference(isDark ? DarkThemeName : LightThemeName);
            ApplyTheme();
            
            Logger.Info("ThemeService", $"Theme set to {(IsDarkTheme ? "dark" : "light")}");
        }

        /// <summary>
        /// Applies the current theme to the application resources.
        /// </summary>
        private void ApplyTheme()
        {
            var themeUri = IsDarkTheme
                ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
                : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

            var mergedDicts = System.Windows.Application.Current.Resources.MergedDictionaries;

            // Find and remove existing theme dictionary
            var existingTheme = mergedDicts.FirstOrDefault(d =>
                d.Source?.OriginalString.Contains("Theme.xaml") == true &&
                !d.Source.OriginalString.Contains("SharedStyles"));

            if (existingTheme != null)
            {
                mergedDicts.Remove(existingTheme);
            }

            // Add the new theme dictionary at the end (so it overrides SharedStyles)
            mergedDicts.Add(new ResourceDictionary { Source = themeUri });
        }

        /// <summary>
        /// Loads the theme preference from storage.
        /// </summary>
        /// <returns>The saved theme name, or "light" as default.</returns>
        private string LoadThemePreference()
        {
            try
            {
                if (File.Exists(_themeFilePath))
                {
                    var theme = File.ReadAllText(_themeFilePath).Trim().ToLowerInvariant();
                    if (theme == DarkThemeName || theme == LightThemeName)
                    {
                        return theme;
                    }
                }
                return LightThemeName;
            }
            catch (Exception ex)
            {
                Logger.Error("ThemeService", $"Failed to load theme preference: {ex.Message}");
                return LightThemeName;
            }
        }

        /// <summary>
        /// Saves the theme preference to storage.
        /// </summary>
        /// <param name="themeName">The theme name to save.</param>
        private void SaveThemePreference(string themeName)
        {
            try
            {
                File.WriteAllText(_themeFilePath, themeName);
            }
            catch (Exception ex)
            {
                Logger.Error("ThemeService", $"Failed to save theme preference: {ex.Message}");
            }
        }
    }
}
