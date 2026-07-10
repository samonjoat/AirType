using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirType.ViewModels;

namespace AirType.Views;

public partial class NotesView : UserControl
{
    public NotesView()
    {
        InitializeComponent();

        if (Application.Current is App app && app.Services != null)
        {
            DataContext = new NotesViewModel(
                app.Services.NotesDatabase,
                app.CapsuleStateManager);
        }

        // Intercept paste operations to add intelligent whitespace
        NotesTextBox.CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Paste,
            OnNotesTextBoxPaste,
            (s, e) => e.CanExecute = true));
    }

    /// <summary>
    /// Toggles capsule widget recording when mic button is clicked.
    /// Click to start, click again to stop. Uses unattended recording mode.
    /// Only shows visual state changes when initiated from this button (not global hotkey).
    /// </summary>
    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        // Focus the TextBox before recording so transcribed text lands correctly
        if (!NotesTextBox.IsFocused)
        {
            NotesTextBox.Focus();
        }

        // Mark this recording as Notes-initiated so visual state updates show
        if (DataContext is NotesViewModel vm)
        {
            vm.SetNotesInitiated();
        }

        if (Application.Current is App app)
        {
            app.ToggleCapsuleRecording();
        }
    }

    /// <summary>
    /// Intercepts paste operations to add leading space if needed.
    /// Ensures transcribed text doesn't concatenate with existing text.
    /// </summary>
    private void OnNotesTextBoxPaste(object sender, ExecutedRoutedEventArgs e)
    {
        if (!Clipboard.ContainsText())
        {
            e.Handled = true;
            return;
        }

        var text = Clipboard.GetText();
        var textBox = (TextBox)sender;

        // Determine insertion point (cursor or start of selection)
        int insertPos = textBox.SelectionLength > 0
            ? textBox.SelectionStart
            : textBox.CaretIndex;

        // Prepend space if preceding character is not whitespace
        if (insertPos > 0 && !char.IsWhiteSpace(textBox.Text[insertPos - 1]))
        {
            text = " " + text;
        }

        // Perform the paste manually
        int selStart = textBox.SelectionStart;
        int selLength = textBox.SelectionLength;

        string currentText = textBox.Text;
        string newText = currentText.Substring(0, selStart) + text + currentText.Substring(selStart + selLength);

        textBox.Text = newText;
        textBox.CaretIndex = selStart + text.Length;

        e.Handled = true;
    }
}
