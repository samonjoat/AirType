using System;
using System.Windows;
using System.Windows.Controls;
using AirType.Models;

namespace AirType.Views
{
    /// <summary>
    /// Edit note form content for use with ModalWindow.
    /// Returns EditNoteResult with the action taken and updated content.
    /// </summary>
    public partial class EditNoteContent : UserControl, IModalContent
    {
        private readonly Note _note;

        public Action<object?>? RequestClose { get; set; }

        public EditNoteContent(Note note)
        {
            InitializeComponent();
            _note = note;
            ContentTextBox.Text = note.Content;
            ContentTextBox.Focus();
            ContentTextBox.SelectAll();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var trimmedContent = ContentTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmedContent))
            {
                // Don't allow empty notes
                return;
            }

            RequestClose?.Invoke(new EditNoteResult
            {
                Action = EditNoteAction.Save,
                Content = trimmedContent,
                Note = _note
            });
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            RequestClose?.Invoke(new EditNoteResult
            {
                Action = EditNoteAction.Cancel,
                Note = _note
            });
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            // Show confirmation dialog centered on page content area
            bool confirmed = ConfirmationDialog.ShowDestructive(
                "Delete Note?",
                "This action cannot be undone. The note will be permanently removed.",
                "Delete",
                "Cancel",
                centerOnContentArea: true);

            if (confirmed)
            {
                RequestClose?.Invoke(new EditNoteResult
                {
                    Action = EditNoteAction.Delete,
                    Note = _note
                });
            }
        }
    }

    /// <summary>
    /// Result returned from the EditNoteContent dialog
    /// </summary>
    public class EditNoteResult
    {
        public EditNoteAction Action { get; set; }
        public string? Content { get; set; }
        public Note? Note { get; set; }
    }

    /// <summary>
    /// Actions that can be taken in the edit note dialog
    /// </summary>
    public enum EditNoteAction
    {
        Cancel,
        Save,
        Delete
    }
}
