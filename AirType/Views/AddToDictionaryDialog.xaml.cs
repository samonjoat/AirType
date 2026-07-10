using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AirType.Models;

namespace AirType.Views;

/// <summary>
/// Dialog for presenting detected text corrections and allowing the user
/// to edit/remove items before adding to the dictionary.
/// V2: Card style with edit/remove icons instead of checkboxes.
/// </summary>
public partial class AddToDictionaryDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TextDiff> Changes { get; }

    public bool HasItems => Changes.Count > 0;

    public IReadOnlyList<TextDiff>? SelectedChanges { get; private set; }

    public AddToDictionaryDialog(IEnumerable<TextDiff> changes)
    {
        Changes = new ObservableCollection<TextDiff>(changes);
        Changes.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasItems));

        InitializeComponent();
        DataContext = this;
    }

    /// <summary>
    /// Shows the dialog and returns the selected changes (or null if skipped/cancelled).
    /// </summary>
    /// <param name="changes">The text differences to display</param>
    /// <param name="centerOnContentArea">If true, centers on page content area instead of full window</param>
    /// <returns>List of selected changes, or null if cancelled</returns>
    public static IReadOnlyList<TextDiff>? Show(IEnumerable<TextDiff> changes, bool centerOnContentArea = false)
    {
        var dialog = new AddToDictionaryDialog(changes);
        dialog.Owner = ModalWindow.GetMainWindow();

        if (centerOnContentArea)
        {
            ModalPositioning.CenterWindowOnContentArea(dialog);
        }

        return dialog.ShowDialog() == true ? dialog.SelectedChanges : null;
    }

    /// <summary>
    /// Start editing a diff item.
    /// </summary>
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is TextDiff diff)
        {
            // Cancel any other editing items
            foreach (var item in Changes.Where(c => c.IsEditing))
            {
                item.IsEditing = false;
            }

            // Start editing this one - initialize BOTH fields
            diff.EditingOriginalText = diff.OriginalText;
            diff.EditingText = diff.CorrectedText;
            diff.IsEditing = true;
        }
    }

    /// <summary>
    /// Save the edited text (both original and corrected).
    /// </summary>
    private void SaveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is TextDiff diff)
        {
            // Validate corrected - don't allow empty
            if (!string.IsNullOrWhiteSpace(diff.EditingText))
            {
                diff.CorrectedText = diff.EditingText.Trim();
            }

            // Save original text (allows multi-word corrections)
            // For Added items, this converts them to Modified if user types original text
            diff.OriginalText = diff.EditingOriginalText?.Trim() ?? string.Empty;

            // If user typed original text for an Added item, convert to Modified
            if (diff.Type == DiffType.Added && !string.IsNullOrWhiteSpace(diff.OriginalText))
            {
                diff.Type = DiffType.Modified;
            }

            diff.IsEditing = false;
        }
    }

    /// <summary>
    /// Cancel editing.
    /// </summary>
    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is TextDiff diff)
        {
            diff.IsEditing = false;
            diff.EditingText = string.Empty;
            diff.EditingOriginalText = string.Empty;
        }
    }

    /// <summary>
    /// Remove a diff item from the list.
    /// </summary>
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is TextDiff diff)
        {
            Changes.Remove(diff);
        }
    }

    /// <summary>
    /// Skip all - close without adding anything.
    /// </summary>
    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        SelectedChanges = null;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Add all remaining items to the dictionary.
    /// </summary>
    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        // Save any active editing first (both original and corrected)
        foreach (var item in Changes.Where(c => c.IsEditing))
        {
            if (!string.IsNullOrWhiteSpace(item.EditingText))
            {
                item.CorrectedText = item.EditingText.Trim();
            }

            // Save original text
            item.OriginalText = item.EditingOriginalText?.Trim() ?? string.Empty;

            // Convert Added to Modified if user typed original text
            if (item.Type == DiffType.Added && !string.IsNullOrWhiteSpace(item.OriginalText))
            {
                item.Type = DiffType.Modified;
            }

            item.IsEditing = false;
        }

        // All remaining items are selected (since user removed ones they don't want)
        SelectedChanges = Changes.ToList();
        DialogResult = true;
        Close();
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
