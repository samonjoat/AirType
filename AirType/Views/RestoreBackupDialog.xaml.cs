using System.Windows;

namespace AirType.Views
{
    /// <summary>
    /// Choice for the restore backup dialog.
    /// </summary>
    public enum RestoreChoice
    {
        Restore,
        StartFresh,
        Exit
    }

    /// <summary>
    /// Dialog shown on startup when database is missing but a backup exists.
    /// Allows user to restore their data or start fresh.
    /// </summary>
    public partial class RestoreBackupDialog : Window
    {
        public RestoreChoice Choice { get; private set; } = RestoreChoice.Exit;

        public RestoreBackupDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the restore backup dialog and returns the user's choice.
        /// This dialog is startup-safe and doesn't require an owner window.
        /// </summary>
        public static RestoreChoice Show(System.DateTime? backupDate = null)
        {
            var dialog = new RestoreBackupDialog();
            if (backupDate.HasValue)
            {
                dialog.BackupDateText.Text = $"Backup from: {backupDate.Value:g}";
            }
            else
            {
                dialog.BackupDateText.Visibility = Visibility.Collapsed;
            }
            
            dialog.ShowDialog();
            return dialog.Choice;
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            Choice = RestoreChoice.Restore;
            Close();
        }

        private void StartFresh_Click(object sender, RoutedEventArgs e)
        {
            Choice = RestoreChoice.StartFresh;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Choice = RestoreChoice.Exit;
            Close();
        }
    }
}
