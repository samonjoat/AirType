using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows;
using System.IO;
using System.Reflection;
using AirType.Views;

namespace AirType.Services
{
    public class TrayManager : IDisposable
    {
        private NotifyIcon? _trayIcon;
        private ContextMenuStrip? _contextMenu;
        private bool _disposed = false;
        private bool _isInitialized;

        public event EventHandler? OpenFullUIRequested;
        public event EventHandler? SettingsRequested;
        public event EventHandler? HistoryRequested;
        public event EventHandler? NotesRequested;
        public event EventHandler? DictionaryRequested;
        public event EventHandler? ExitRequested;

        public bool IsInitialized => _isInitialized;

        public void Initialize()
        {
            try
            {
                _isInitialized = false;

                // Create the tray icon
                _trayIcon = new NotifyIcon();
                
                // Load the AirType application icon for the system tray
                var iconUri = new Uri("airtype.ico", UriKind.Relative);
                using (var iconStream = System.Windows.Application.GetResourceStream(iconUri).Stream)
                {
                    _trayIcon.Icon = new Icon(iconStream, SystemInformation.SmallIconSize);
                }
                _trayIcon.Text = "AirType";
                _trayIcon.Visible = true;

                // Create context menu
                CreateContextMenu();
                
                // Attach context menu to tray icon
                _trayIcon.ContextMenuStrip = _contextMenu;
                
                // Handle tray icon events
                _trayIcon.DoubleClick += OnTrayIconDoubleClick;
                _trayIcon.MouseClick += OnTrayIconMouseClick;

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                NotificationDialog.ShowError("AirType Error", $"Failed to initialize system tray: {ex.Message}");
                _isInitialized = false;
            }
        }

        private void CreateContextMenu()
        {
            _contextMenu = new ContextMenuStrip();
            
            // Add "Open" menu item
            var openItem = new ToolStripMenuItem("Open Dashboard");
            openItem.Click += OnOpenMenuItemClick;
            _contextMenu.Items.Add(openItem);

            // Add "History" menu item
            var historyItem = new ToolStripMenuItem("View History");
            historyItem.Click += OnHistoryMenuItemClick;
            _contextMenu.Items.Add(historyItem);

            // Add "Notes" menu item
            var notesItem = new ToolStripMenuItem("View Notes");
            notesItem.Click += OnNotesMenuItemClick;
            _contextMenu.Items.Add(notesItem);

            // Add "Dictionary" menu item
            var dictionaryItem = new ToolStripMenuItem("Custom Dictionary");
            dictionaryItem.Click += OnDictionaryMenuItemClick;
            _contextMenu.Items.Add(dictionaryItem);

            // Add "Settings" menu item
            var settingsItem = new ToolStripMenuItem("Settings");
            settingsItem.Click += OnSettingsMenuItemClick;
            _contextMenu.Items.Add(settingsItem);
            
            // Add separator
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            // Add "Exit" menu item
            var exitItem = new ToolStripMenuItem("Exit Application");
            exitItem.Click += OnExitMenuItemClick;
            _contextMenu.Items.Add(exitItem);
        }

        private void OnHistoryMenuItemClick(object? sender, EventArgs e)
        {
            HistoryRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnSettingsMenuItemClick(object? sender, EventArgs e)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnNotesMenuItemClick(object? sender, EventArgs e)
        {
            NotesRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnDictionaryMenuItemClick(object? sender, EventArgs e)
        {
            DictionaryRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnTrayIconDoubleClick(object? sender, EventArgs e)
        {
            // Double-click shows/hides the main UI
            OpenFullUIRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnTrayIconMouseClick(object? sender, MouseEventArgs e)
        {
            // Handle single clicks if needed
            // For now, we'll only handle double-clicks and context menu
        }

        private void OnOpenMenuItemClick(object? sender, EventArgs e)
        {
            OpenFullUIRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnExitMenuItemClick(object? sender, EventArgs e)
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeout = 3000)
        {
            if (_trayIcon != null && _trayIcon.Visible)
            {
                _trayIcon.ShowBalloonTip(timeout, title, text, icon);
            }
        }

        public void Hide()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
            }
        }

        public void Show()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = true;
            }
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
                _trayIcon?.Dispose();
                _contextMenu?.Dispose();
                _isInitialized = false;
                _disposed = true;
            }
        }

        ~TrayManager()
        {
            Dispose(false);
        }
    }
}
