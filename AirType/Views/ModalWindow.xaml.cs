using System.Linq;
using System.Windows;
using AirType.Services;

namespace AirType.Views
{
    /// <summary>
    /// Generic modal window host that can display any content.
    /// Use the static Show methods for consistent modal behavior across the app.
    /// </summary>
    public partial class ModalWindow : Window
    {
        /// <summary>
        /// The result returned by the modal content (e.g., true/false for confirmations, data objects for forms)
        /// </summary>
        public object? Result { get; set; }

        /// <summary>
        /// Whether the close button should be visible
        /// </summary>
        public bool ShowCloseButton
        {
            get => CloseButton.Visibility == Visibility.Visible;
            set => CloseButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        public ModalWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows content in a modal window and returns the result.
        /// </summary>
        /// <typeparam name="T">Expected result type</typeparam>
        /// <param name="content">The content to display (UserControl, StackPanel, etc.)</param>
        /// <param name="owner">Parent window for centering. Defaults to MainWindow if null.</param>
        /// <param name="showCloseButton">Whether to show the X close button</param>
        /// <param name="centerOnContentArea">If true, centers on page content area instead of full window</param>
        /// <returns>The Result property cast to T, or default if null/cancelled</returns>
        public static T? Show<T>(FrameworkElement content, Window? owner = null, bool showCloseButton = true, bool centerOnContentArea = false)
        {
            var modal = new ModalWindow();
            modal.ContentHost.Content = content;
            modal.Owner = owner ?? GetMainWindow();
            modal.ShowCloseButton = showCloseButton;

            // If content implements IModalContent, wire up the close action
            if (content is IModalContent modalContent)
            {
                modalContent.RequestClose = (result) =>
                {
                    modal.Result = result;
                    modal.Close();
                };
            }

            // Apply page-level centering if requested
            if (centerOnContentArea)
            {
                ModalPositioning.CenterWindowOnContentArea(modal);
            }

            modal.ShowDialog();
            return modal.Result is T typedResult ? typedResult : default;
        }

        /// <summary>
        /// Shows content in a modal window (non-generic version for void results)
        /// </summary>
        public static void Show(FrameworkElement content, Window? owner = null, bool showCloseButton = true, bool centerOnContentArea = false)
        {
            Show<object>(content, owner, showCloseButton, centerOnContentArea);
        }

        /// <summary>
        /// Gets the main application window (not CapsuleWidget or other floating windows)
        /// </summary>
        public static Window? GetMainWindow()
        {
            return Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            Close();
        }
    }

    /// <summary>
    /// Interface for modal content that needs to close the modal and return a result
    /// </summary>
    public interface IModalContent
    {
        /// <summary>
        /// Action to call when the content wants to close the modal.
        /// Pass the result object (or null to cancel).
        /// </summary>
        Action<object?>? RequestClose { get; set; }
    }

    /// <summary>
    /// Helper for calculating page-level modal positioning.
    /// Centers modals on the ContentArea instead of the full MainWindow.
    /// </summary>
    public static class ModalPositioning
    {
        /// <summary>
        /// Gets the screen-space bounds of the ContentArea in MainWindow.
        /// Uses TransformToAncestor for reliable multi-monitor positioning.
        /// </summary>
        public static Rect? GetContentAreaBounds()
        {
            var mainWindow = ModalWindow.GetMainWindow();
            if (mainWindow == null) return null;

            var contentArea = mainWindow.FindName("ContentArea") as FrameworkElement;
            if (contentArea == null) return null;

            try
            {
                // Get ContentArea position relative to MainWindow using TransformToAncestor
                var transform = contentArea.TransformToAncestor(mainWindow);
                var relativePosition = transform.Transform(new Point(0, 0));

                // Calculate screen position by adding MainWindow's position
                double screenLeft = mainWindow.Left + relativePosition.X;
                double screenTop = mainWindow.Top + relativePosition.Y;

                return new Rect(screenLeft, screenTop, contentArea.ActualWidth, contentArea.ActualHeight);
            }
            catch
            {
                // Fallback to PointToScreen if TransformToAncestor fails
                var topLeft = contentArea.PointToScreen(new Point(0, 0));
                return new Rect(topLeft.X, topLeft.Y, contentArea.ActualWidth, contentArea.ActualHeight);
            }
        }

        /// <summary>
        /// Centers a window on the ContentArea after it is fully rendered.
        /// </summary>
        public static void CenterWindowOnContentArea(Window window)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;

            // Hide window initially to prevent visible jump during positioning
            window.Opacity = 0;

            // Use ContentRendered - fires AFTER content is fully rendered and window has final size
            window.ContentRendered += (s, e) =>
            {
                var mainWindow = ModalWindow.GetMainWindow();
                Logger.Debug("ModalPositioning", $"MainWindow position: Left={mainWindow?.Left}, Top={mainWindow?.Top}");

                var bounds = GetContentAreaBounds();

                // Debug logging
                Logger.Debug("ModalPositioning", $"ContentArea bounds: {bounds}");
                Logger.Debug("ModalPositioning", $"Modal size: {window.ActualWidth} x {window.ActualHeight}");

                if (!bounds.HasValue)
                {
                    Logger.Warn("ModalPositioning", "ContentArea bounds not available, using fallback");
                    // Fallback: center on owner window
                    if (window.Owner != null)
                    {
                        window.Left = window.Owner.Left + (window.Owner.Width - window.ActualWidth) / 2;
                        window.Top = window.Owner.Top + (window.Owner.Height - window.ActualHeight) / 2;
                    }
                }
                else
                {
                    double left = bounds.Value.Left + (bounds.Value.Width - window.ActualWidth) / 2;
                    double top = bounds.Value.Top + (bounds.Value.Height - window.ActualHeight) / 2;

                    Logger.Debug("ModalPositioning", $"Calculated position: Left={left}, Top={top}");

                    window.Left = left;
                    window.Top = top;
                }

                // Reveal window after positioning - appears instantly in correct spot
                window.Opacity = 1;
            };
        }
    }
}
