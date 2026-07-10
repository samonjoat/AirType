using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace AirType.Views
{
    /// <summary>
    /// Reusable confirmation dialog following the app's design system.
    /// Use the static Show methods for easy invocation.
    /// </summary>
    public partial class ConfirmationDialog : Window
    {
        public bool Confirmed { get; private set; }

        public ConfirmationDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows a simple confirmation dialog.
        /// </summary>
        /// <param name="owner">Parent window for centering. Defaults to MainWindow if null.</param>
        /// <param name="centerOnContentArea">If true, centers on page content area instead of full window</param>
        public static bool Show(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel", Window? owner = null, bool centerOnContentArea = false)
        {
            var dialog = new ConfirmationDialog();
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;
            dialog.ConfirmButton.Content = confirmText;
            dialog.CancelButton.Content = cancelText;
            dialog.Owner = owner ?? ModalWindow.GetMainWindow();

            if (centerOnContentArea)
            {
                ModalPositioning.CenterWindowOnContentArea(dialog);
            }

            dialog.ShowDialog();
            return dialog.Confirmed;
        }

        /// <summary>
        /// Shows a destructive action confirmation (with red styling and icon).
        /// </summary>
        /// <param name="owner">Parent window for centering. Defaults to MainWindow if null.</param>
        /// <param name="centerOnContentArea">If true, centers on page content area instead of full window</param>
        public static bool ShowDestructive(string title, string message, string confirmText = "Delete", string cancelText = "Cancel", PackIconKind icon = PackIconKind.TrashCanOutline, Window? owner = null, bool centerOnContentArea = false)
        {
            var dialog = new ConfirmationDialog();
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;
            dialog.ConfirmButton.Content = confirmText;
            dialog.CancelButton.Content = cancelText;
            dialog.Owner = owner ?? ModalWindow.GetMainWindow();

            // Show icon with error styling
            dialog.IconContainer.Visibility = Visibility.Visible;
            dialog.IconContainer.Background = (Brush)Application.Current.Resources["ErrorSubtleBrush"];
            dialog.DialogIcon.Kind = icon;
            dialog.DialogIcon.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];

            // Style confirm button as danger
            dialog.ConfirmButton.Background = (Brush)Application.Current.Resources["ErrorBrush"];
            dialog.ConfirmButton.Foreground = (Brush)Application.Current.Resources["BgBrush"];
            dialog.ConfirmButton.BorderBrush = (Brush)Application.Current.Resources["ErrorBrush"];

            if (centerOnContentArea)
            {
                ModalPositioning.CenterWindowOnContentArea(dialog);
            }

            dialog.ShowDialog();
            return dialog.Confirmed;
        }

        /// <summary>
        /// Shows a warning confirmation (with warning styling and icon).
        /// </summary>
        /// <param name="owner">Parent window for centering. Defaults to MainWindow if null.</param>
        /// <param name="centerOnContentArea">If true, centers on page content area instead of full window</param>
        public static bool ShowWarning(string title, string message, string confirmText = "Continue", string cancelText = "Cancel", PackIconKind icon = PackIconKind.AlertOutline, Window? owner = null, bool centerOnContentArea = false)
        {
            var dialog = new ConfirmationDialog();
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;
            dialog.ConfirmButton.Content = confirmText;
            dialog.CancelButton.Content = cancelText;
            dialog.Owner = owner ?? ModalWindow.GetMainWindow();

            // Show icon with warning styling
            dialog.IconContainer.Visibility = Visibility.Visible;
            dialog.IconContainer.Background = (Brush)Application.Current.Resources["BgMutedBrush"];
            dialog.DialogIcon.Kind = icon;
            dialog.DialogIcon.Foreground = (Brush)Application.Current.Resources["WarningBrush"];

            if (centerOnContentArea)
            {
                ModalPositioning.CenterWindowOnContentArea(dialog);
            }

            dialog.ShowDialog();
            return dialog.Confirmed;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
