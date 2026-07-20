using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using PaperNote.Desktop.Models;

namespace PaperNote.Desktop.ViewModels;

public sealed class NotebookCardViewModel
{
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public required string PageCountText { get; init; }
    public required string ModifiedText { get; init; }
    public required string FolderText { get; init; }
    public required Brush CoverBrush { get; init; }
    public ImageSource? CoverThumbnail { get; init; }
    public bool IsInTrash { get; init; }
    public string MatchText { get; init; } = string.Empty;
    public Visibility MatchVisibility => string.IsNullOrWhiteSpace(MatchText) ? Visibility.Collapsed : Visibility.Visible;
    public bool CanOpen => !IsInTrash;
    public Visibility NormalActionsVisibility => IsInTrash ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TrashActionsVisibility => IsInTrash ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class LibraryFilterViewModel
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public required int Count { get; init; }
    public string CountText => Count.ToString();
}

public sealed class PageItemViewModel : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private string _title = string.Empty;
    private bool _isBookmarked;

    public required Guid Id { get; init; }
    public required int Number { get; init; }
    public string NumberText => $"第 {Number} 页";
    public string DisplayTitle => string.IsNullOrWhiteSpace(_title) ? "未命名页面" : _title;
    public string BookmarkGlyph => _isBookmarked ? "★" : "☆";

    public string Title
    {
        get => _title;
        set
        {
            var normalized = value ?? string.Empty;
            if (_title == normalized) return;
            _title = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public bool IsBookmarked
    {
        get => _isBookmarked;
        set
        {
            if (_isBookmarked == value) return;
            _isBookmarked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BookmarkGlyph));
        }
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
