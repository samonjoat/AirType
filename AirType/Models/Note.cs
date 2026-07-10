using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AirType.Models;

public class Note : INotifyPropertyChanged
{
    private int _id;
    private DateTime _date;
    private string _content = string.Empty;
    private DateTime? _editedAt;

    public int Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public DateTime Date
    {
        get => _date;
        set { _date = value; OnPropertyChanged(); OnPropertyChanged(nameof(DateDisplay)); }
    }

    public string Content
    {
        get => _content;
        set
        {
            _content = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(DisplayBody));
        }
    }

    /// <summary>
    /// Timestamp when note was last edited (null if never edited)
    /// </summary>
    public DateTime? EditedAt
    {
        get => _editedAt;
        set { _editedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEdited)); }
    }

    /// <summary>
    /// Returns true if the note has been edited after creation
    /// </summary>
    public bool IsEdited => EditedAt.HasValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Computed properties for display
    public string DisplayTitle => SplitContent().Title;
    public string DisplayBody => SplitContent().Body;

    public string DateDisplay
    {
        get
        {
            var today = DateTime.Today;
            if (Date.Date == today)
            {
                return $"Today {Date:HH:mm}";
            }

            if (Date.Date == today.AddDays(-1))
            {
                return "Yesterday";
            }

            if (Date.Date > today.AddDays(-7))
            {
                return Date.ToString("ddd");
            }

            return Date.ToString("MMM d");
        }
    }

    private (string Title, string Body) SplitContent()
    {
        var text = Content.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return ("Untitled note", string.Empty);
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length > 1)
        {
            return (lines[0], string.Join(" ", lines.Skip(1)).Trim());
        }

        var singleLine = lines[0];
        var sentenceEnd = singleLine.IndexOfAny(['.', '!', '?']);
        if (sentenceEnd > 12 && sentenceEnd < 70 && sentenceEnd < singleLine.Length - 1)
        {
            return (singleLine[..(sentenceEnd + 1)].Trim(), singleLine[(sentenceEnd + 1)..].Trim());
        }

        if (singleLine.Length > 56)
        {
            return ($"{singleLine[..53].TrimEnd()}...", singleLine);
        }

        return (singleLine, string.Empty);
    }

    public string TimeAgo
    {
        get
        {
            var now = DateTime.Now;
            var diff = now - Date;

            if (diff.TotalMinutes < 1)
                return "Just Now";
            else if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes} minutes ago";
            else if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours} hours ago";
            else if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays} days ago";
            else
                return Date.ToString("MMM d");
        }
    }
}
