using System;
using System.Windows;
using System.Windows.Media;
using AirType.Services;
using MaterialDesignThemes.Wpf;

namespace AirType.Views
{
    /// <summary>
    /// Reusable notification dialog for single-button messages (Success, Info, Warning, Error).
    /// Use the static Show methods for easy invocation.
    /// </summary>
    public partial class NotificationDialog : Window
    {
        public NotificationDialog()
        {
            InitializeComponent();
        }

        private static bool TryShowToast(string title, string message, ToastType type, bool centerOnContentArea)
        {
            if (!centerOnContentArea)
            {
                return false;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return false;
            }

            void showToast() => ToastService.Instance.Show(message, type, title);

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(showToast));
                return true;
            }

            if (ModalWindow.GetMainWindow()?.IsVisible != true)
            {
                return false;
            }

            showToast();
            return true;
        }

        /// <summary>
        /// Shows a success notification with green/accent styling.
        /// </summary>
        /// <param name="title">Title text</param>
        /// <param name="message">Message content</param>
        /// <param name="buttonText">OK button text (default: "OK")</param>
        /// <param name="icon">Icon to display (default: CheckCircleOutline)</param>
        /// <param name="owner">Parent window for centering. Defaults to MainWindow if null.</param>
        /// <param name="centerOnContentArea">If true, centers on page content area instead of full window</param>
        public static void ShowSuccess(string title, string message, string buttonText = "OK", PackIconKind icon = PackIconKind.CheckCircleOutline, Window? owner = null, bool centerOnContentArea = false)
        {
            if (TryShowToast(title, message, ToastType.Success, centerOnContentArea))
            {
                return;
            }

            var dialog = new NotificationDialog();
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;
            dialog.OKButton.Content = buttonText;
            dialog.Owner = owner ?? ModalWindow.GetMainWindow();

            // Success styling
            dialog.IconContainer.Background = (Brush)Application.Current.Resources["SuccessSubtleBrush"];
            dialog.DialogIcon.Kind = icon;
            dialog.DialogIcon.Foreground = (Brush)Application.Current.Resources["SuccessBrush"];

            if (centerOnContentArea)
            {
                ModalPositioning.CenterWindowOnContentArea(dialog);
            }

            dialog.ShowDialog();
        }

        /// <summary>
        /// Shows an informational notification with blue/neutral styling.
        /// </summary>
        /// <param name="title">Title text</param>
        /// <param name="message">Message content</param>
        /// <param name="buttonText">OK button text (default: "OK")</param>
        /// <param name="icon">Icon to display (default: InformationOutline)</param>
        /// <param name="owner">Parent window for centering. Defaults to MainWindow if null.</param>
        /// <param name="centerOnContentArea">If true, centers on page content area instead of full window</param>
        public static void ShowInfo(string title, string message, string buttonText = "OK", PackIconKind icon = PackIconKind.InformationOutline, Window? owner = null, bool centerOnContentArea = false)
        {
            if (TryShowToast(title, message, ToastType.Info, centerOnContentArea))
            {
                return;
            }

            var dialog = new NotificationDialog();
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;
            dialog.OKButton.Content = buttonText;
            dialog.Owner = owner ?? ModalWindow.GetMainWindow();

            // Info styling
            dialog.IconContainer.Background = (Brush)Application.Current.Resources["AccentSubtleBrush"];
            dialog.DialogIcon.Kind = icon;
            dialog.DialogIcon.Foreground = (Brush)Application.Current.Resources["AccentBrush"];

            if (centerOnContentArea)
            {
                ModalPositioning.CenterWindowOnContentArea(dialog);
            }

            dialog.ShowDialog();
        }

        /// <summary>
        /// Shows a warning notification with orange/yellow styling.
        /// </summary>
        /// <param name="title">Title text</param>
        /// <param name="message">Message content</param>
        /// <param name="buttonText">OK button text (default: "OK")</param>
        /// <param name="icon">Icon to display (default: AlertOutline)</param>
        /// <param name="owner">Parent window for centering. Defaults to MainWindow if null.</param>
        /// <param name="centerOnContentArea">If true, centers on page content area instead of full window</param>
        public static void ShowWarning(string title, string message, string buttonText = "OK", PackIconKind icon = PackIconKind.AlertOutline, Window? owner = null, bool centerOnContentArea = false)
        {
            if (TryShowToast(title, message, ToastType.Warning, centerOnContentArea))
            {
                return;
            }

            var dialog = new NotificationDialog();
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;
            dialog.OKButton.Content = buttonText;
            dialog.Owner = owner ?? ModalWindow.GetMainWindow();

            // Warning styling
            dialog.IconContainer.Background = (Brush)Application.Current.Resources["BgMutedBrush"];
            dialog.DialogIcon.Kind = icon;
            dialog.DialogIcon.Foreground = (Brush)Application.Current.Resources["WarningBrush"];

            if (centerOnContentArea)
            {
                ModalPositioning.CenterWindowOnContentArea(dialog);
            }

            dialog.ShowDialog();
        }

        /// <summary>
        /// Shows an error notification with red styling.
        /// </summary>
        /// <param name="title">Title text</param>
        /// <param name="message">Message content</param>
        /// <param name="buttonText">OK button text (default: "OK")</param>
        /// <param name="icon">Icon to display (default: AlertCircleOutline)</param>
        /// <param name="owner">Parent window for centering. Defaults to MainWindow if null.</param>
        /// <param name="centerOnContentArea">If true, centers on page content area instead of full window</param>
        public static void ShowError(string title, string message, string buttonText = "OK", PackIconKind icon = PackIconKind.AlertCircleOutline, Window? owner = null, bool centerOnContentArea = false)
        {
            if (TryShowToast(title, message, ToastType.Error, centerOnContentArea))
            {
                return;
            }

            var dialog = new NotificationDialog();
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;
            dialog.OKButton.Content = buttonText;
            dialog.Owner = owner ?? ModalWindow.GetMainWindow();

            // Error styling
            dialog.IconContainer.Background = (Brush)Application.Current.Resources["ErrorSubtleBrush"];
            dialog.DialogIcon.Kind = icon;
            dialog.DialogIcon.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];

            if (centerOnContentArea)
            {
                ModalPositioning.CenterWindowOnContentArea(dialog);
            }

            dialog.ShowDialog();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
