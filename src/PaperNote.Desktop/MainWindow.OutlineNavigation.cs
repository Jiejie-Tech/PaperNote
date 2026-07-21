using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PaperNote.Core.Models;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<OutlineEntryView> _outlineEntries = [];

    private sealed class OutlineEntryView
    {
        public Guid Id { get; init; }
        public int PageNumber { get; init; }
        public int Level { get; init; }
        public string Title { get; init; } = string.Empty;
        public string PageText => $"第 {PageNumber} 页";
        public string LevelText => $"{Level} 级";
        public Thickness Indent => new((Level - 1) * 24, 2, 0, 2);
    }

    private void InitializeOutlineNavigation()
    {
        OutlineListBox.ItemsSource = _outlineEntries;
        UpdateOutlineStatus();
    }

    private void OpenOutline_Click(object sender, RoutedEventArgs e) => OpenOutline();

    private void OpenOutline()
    {
        if (_currentNotebook is null) return;
        CaptureCurrentPage();
        OutlineOverlay.Visibility = Visibility.Visible;
        RefreshOutlineItems(_currentPage?.Id);
        OutlineSearchBox.Focus();
        StatusText.Text = $"笔记目录 · {_currentNotebook.Pages.Count(page => page.OutlineLevel > 0)} 个条目";
    }

    private void CloseOutline_Click(object sender, RoutedEventArgs e) => CloseOutline();

    private void CloseOutline()
    {
        OutlineOverlay.Visibility = Visibility.Collapsed;
        InkSurface.Focus();
        UpdatePageNavigationStatus();
    }

    private void OutlineSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || OutlineOverlay.Visibility != Visibility.Visible) return;
        RefreshOutlineItems(_currentPage?.Id);
    }

    private void RefreshOutlineItems(Guid? selectedId = null)
    {
        _outlineEntries.Clear();
        if (_currentNotebook is null)
        {
            UpdateOutlineStatus();
            return;
        }

        var query = OutlineSearchBox.Text.Trim();
        for (var index = 0; index < _currentNotebook.Pages.Count; index++)
        {
            var page = _currentNotebook.Pages[index];
            if (page.OutlineLevel <= 0) continue;
            var title = string.IsNullOrWhiteSpace(page.Title) ? $"第 {index + 1} 页" : page.Title;
            if (query.Length > 0 &&
                !title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !(index + 1).ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            _outlineEntries.Add(new OutlineEntryView
            {
                Id = page.Id,
                PageNumber = index + 1,
                Level = page.OutlineLevel,
                Title = title
            });
        }

        var preferred = _outlineEntries.FirstOrDefault(entry => entry.Id == selectedId) ?? _outlineEntries.FirstOrDefault();
        OutlineListBox.SelectedItem = preferred;
        if (preferred is not null) OutlineListBox.ScrollIntoView(preferred);
        UpdateOutlineStatus();
    }

    private void RefreshOutlineIfVisible()
    {
        if (OutlineOverlay.Visibility == Visibility.Visible) RefreshOutlineItems(_currentPage?.Id);
    }

    private void UpdateOutlineStatus()
    {
        if (OutlineStatusText is null) return;
        var total = _currentNotebook?.Pages.Count(page => page.OutlineLevel > 0) ?? 0;
        OutlineStatusText.Text = _outlineEntries.Count == total
            ? $"目录中有 {total} 个页面"
            : $"找到 {_outlineEntries.Count} 个 · 目录共 {total} 个页面";
    }

    private void OutlineListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => JumpToSelectedOutlineEntry();
    private void OutlineJump_Click(object sender, RoutedEventArgs e) => JumpToSelectedOutlineEntry();

    private bool JumpToSelectedOutlineEntry()
    {
        if (OutlineListBox.SelectedItem is not OutlineEntryView entry) return false;
        if (!NavigateToPage(entry.PageNumber)) return false;
        CloseOutline();
        StatusText.Text = $"已从目录跳转到第 {entry.PageNumber} 页";
        return true;
    }

    private void CurrentPageOutline_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage is null || sender is not Button button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        menu.Items.Add(CreateOutlineLevelMenuItem("不加入目录", 0));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateOutlineLevelMenuItem("一级标题", 1));
        menu.Items.Add(CreateOutlineLevelMenuItem("二级标题", 2));
        menu.Items.Add(CreateOutlineLevelMenuItem("三级标题", 3));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("打开笔记目录", "Ctrl+Shift+D", (_, _) => OpenOutline(), true));
        menu.IsOpen = true;
    }

    private MenuItem CreateOutlineLevelMenuItem(string header, int level)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = _currentPage?.OutlineLevel == level,
            IsEnabled = !_isReadOnly
        };
        item.Click += (_, _) => SetCurrentPageOutlineLevel(level);
        return item;
    }

    private bool SetCurrentPageOutlineLevel(int level)
    {
        if (_currentPage is null || _currentNotebook is null || _isReadOnly) return false;
        level = Math.Clamp(level, 0, 3);
        if (_currentPage.OutlineLevel == level) return false;
        _currentPage.OutlineLevel = level;
        _currentPage.ModifiedAt = DateTimeOffset.Now;
        UpdateCurrentPageOutlineButton(_currentPage);
        RefreshOutlineIfVisible();
        MarkDirty();
        StatusText.Text = level == 0 ? "当前页已移出目录" : $"当前页已设为 {level} 级目录";
        return true;
    }

    private void UpdateCurrentPageOutlineButton(NotebookPage page)
    {
        CurrentPageOutlineButton.Content = page.OutlineLevel switch
        {
            1 => "一级⌄",
            2 => "二级⌄",
            3 => "三级⌄",
            _ => "目录⌄"
        };
        CurrentPageOutlineButton.ToolTip = page.OutlineLevel == 0
            ? "把当前页加入笔记目录"
            : $"当前为 {page.OutlineLevel} 级目录，点击修改";
    }
}
