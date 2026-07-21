using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private sealed class NotebookTabState
    {
        public required string FilePath { get; init; }
        public required string Title { get; set; }
    }

    private readonly List<NotebookTabState> _openNotebookTabs = [];
    private readonly WorkspaceStateService _workspaceStateService;
    private bool _isRestoringWorkspaceState;

    private void InitializeWorkspaceNavigation()
    {
        UpdateNotebookTabsButton();
        ResetPageVisitHistory();
    }

    private void RegisterOpenNotebookTab(string filePath, string title)
    {
        var existing = _openNotebookTabs.FirstOrDefault(tab => string.Equals(tab.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            if (_openNotebookTabs.Count >= 8) _openNotebookTabs.RemoveAt(0);
            _openNotebookTabs.Add(new NotebookTabState { FilePath = filePath, Title = title });
        }
        else
        {
            existing.Title = title;
        }
        UpdateNotebookTabsButton();
        SaveWorkspaceStateSoon();
    }

    private void UpdateCurrentNotebookTabTitle()
    {
        if (string.IsNullOrWhiteSpace(_currentNotebookPath) || _currentNotebook is null) return;
        var tab = _openNotebookTabs.FirstOrDefault(item => string.Equals(item.FilePath, _currentNotebookPath, StringComparison.OrdinalIgnoreCase));
        if (tab is not null) tab.Title = _currentNotebook.Title;
        UpdateNotebookTabsButton();
        SaveWorkspaceStateSoon();
    }

    private void RemoveNotebookTab(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        _openNotebookTabs.RemoveAll(tab => string.Equals(tab.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        UpdateNotebookTabsButton();
        SaveWorkspaceStateSoon();
    }

    private void UpdateNotebookTabsButton()
    {
        if (NotebookTabsButton is null) return;
        NotebookTabsButton.Content = _openNotebookTabs.Count == 0 ? "标签页" : $"标签页 {_openNotebookTabs.Count}";
        NotebookTabsButton.ToolTip = _openNotebookTabs.Count == 0
            ? "打开的笔记本会保留在这里，方便快速切换"
            : $"已打开 {_openNotebookTabs.Count} 本笔记；Ctrl+Tab 切换，重启后仍保留";
        UpdateVisibleNotebookTabs();
    }

    private void UpdateVisibleNotebookTabs()
    {
        if (NotebookTabStripPanel is null) return;
        NotebookTabStripPanel.Children.Clear();
        if (_openNotebookTabs.Count == 0)
        {
            NotebookTabStripPanel.Children.Add(new TextBlock
            {
                Text = "打开笔记本后会显示可切换标签",
                Foreground = new SolidColorBrush(Color.FromRgb(139, 146, 160)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            return;
        }

        foreach (var tab in _openNotebookTabs)
        {
            var isCurrent = string.Equals(tab.FilePath, _currentNotebookPath, StringComparison.OrdinalIgnoreCase);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var open = new Button
            {
                Content = (isCurrent ? "● " : string.Empty) + tab.Title,
                Tag = tab.FilePath,
                Height = 27,
                MinWidth = 96,
                MaxWidth = 210,
                Padding = new Thickness(10, 0, 7, 0),
                Background = new SolidColorBrush(isCurrent ? Color.FromRgb(231, 238, 255) : Color.FromRgb(255, 255, 255)),
                Foreground = new SolidColorBrush(isCurrent ? Color.FromRgb(49, 92, 187) : Color.FromRgb(76, 83, 96)),
                BorderBrush = new SolidColorBrush(isCurrent ? Color.FromRgb(170, 190, 235) : Color.FromRgb(218, 222, 230)),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = tab.Title
            };
            open.Click += SwitchNotebookTab_Click;
            var close = new Button
            {
                Content = "×",
                Tag = tab.FilePath,
                Width = 27,
                Height = 27,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(113, 120, 133)),
                ToolTip = $"关闭 {tab.Title} 标签"
            };
            close.Click += CloseNotebookTab_Click;
            panel.Children.Add(open);
            panel.Children.Add(close);
            NotebookTabStripPanel.Children.Add(new Border { Child = panel, Margin = new Thickness(0, 0, 7, 0) });
        }
    }

    private async void CloseNotebookTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filePath }) return;
        if (string.Equals(filePath, _currentNotebookPath, StringComparison.OrdinalIgnoreCase))
        {
            await CloseCurrentNotebookTabSafelyAsync();
            return;
        }
        RemoveNotebookTab(filePath);
    }

    private async Task RestoreWorkspaceStateAsync()
    {
        _isRestoringWorkspaceState = true;
        try
        {
            var state = await _workspaceStateService.LoadAsync();
            _librarySort = state.LibrarySort;
            if (LibrarySortCombo is not null)
            {
                foreach (var item in LibrarySortCombo.Items.OfType<ComboBoxItem>())
                {
                    if (string.Equals(item.Tag as string, _librarySort, StringComparison.OrdinalIgnoreCase))
                    {
                        LibrarySortCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            var available = _storedNotebooks.Where(item => !item.Document.IsInTrash)
                .ToDictionary(item => Path.GetFullPath(item.FilePath), item => item.Document.Title, StringComparer.OrdinalIgnoreCase);
            _openNotebookTabs.Clear();
            foreach (var saved in state.Tabs)
            {
                var fullPath = Path.GetFullPath(saved.FilePath);
                if (!available.TryGetValue(fullPath, out var title)) continue;
                _openNotebookTabs.Add(new NotebookTabState { FilePath = fullPath, Title = title });
            }
            UpdateNotebookTabsButton();
            ApplyLibraryFilter();
        }
        finally { _isRestoringWorkspaceState = false; }
    }

    private void SaveWorkspaceStateSoon()
    {
        if (_isRestoringWorkspaceState || _isCloseRequested || _isClosed || !IsLoaded) return;
        _ = SaveWorkspaceStateSafelyAsync();
    }

    private async Task SaveWorkspaceStateSafelyAsync()
    {
        try
        {
            await SaveWorkspaceStateAsync();
        }
        catch (Exception exception)
        {
            if (!_isClosed && !_isCloseRequested && StatusText is not null)
            {
                StatusText.Text = $"工作区状态保存失败：{exception.Message}";
            }
        }
    }

    private Task SaveWorkspaceStateAsync()
    {
        var state = new WorkspaceState
        {
            LibrarySort = _librarySort,
            ActiveNotebookPath = _currentNotebookPath ?? string.Empty,
            Tabs = _openNotebookTabs.Select(tab => new WorkspaceNotebookTab { FilePath = tab.FilePath, Title = tab.Title }).ToList()
        };
        return _workspaceStateService.SaveAsync(state);
    }

    private void NotebookTabs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu();
        if (_openNotebookTabs.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "还没有打开的笔记本", IsEnabled = false });
        }
        else
        {
            foreach (var tab in _openNotebookTabs.ToArray())
            {
                var isCurrent = string.Equals(tab.FilePath, _currentNotebookPath, StringComparison.OrdinalIgnoreCase);
                var item = new MenuItem
                {
                    Header = (isCurrent ? "✓ " : string.Empty) + tab.Title,
                    Tag = tab.FilePath,
                    FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal
                };
                item.Click += SwitchNotebookTab_Click;
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("关闭当前标签", "Ctrl+W", async (_, _) => await CloseCurrentNotebookTabSafelyAsync(), _currentNotebookPath is not null));
            menu.Items.Add(CreateMenuItem("返回书架", "", BackToLibrary_Click, true));
        }
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async void SwitchNotebookTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string filePath }) return;
        if (string.Equals(filePath, _currentNotebookPath, StringComparison.OrdinalIgnoreCase))
        {
            ShowEditor();
            StatusText.Text = "已经位于这个笔记本";
            return;
        }
        await OpenNotebookAsync(filePath);
    }

    private async Task CloseCurrentNotebookTabAsync()
    {
        var closingPath = _currentNotebookPath;
        if (string.IsNullOrWhiteSpace(closingPath)) return;
        if (_isDirty) await SaveNotebookAsync();
        var closingIndex = _openNotebookTabs.FindIndex(tab => string.Equals(tab.FilePath, closingPath, StringComparison.OrdinalIgnoreCase));
        RemoveNotebookTab(closingPath);
        var next = _openNotebookTabs.Count == 0 ? null : _openNotebookTabs[Math.Clamp(closingIndex, 0, _openNotebookTabs.Count - 1)];
        if (next is not null)
        {
            await OpenNotebookAsync(next.FilePath);
            return;
        }
        _currentNotebook = null;
        _currentPage = null;
        _currentNotebookPath = null;
        ResetPageVisitHistory();
        await RefreshLibraryAsync();
        ShowLibrary();
    }

    private async Task CloseCurrentNotebookTabSafelyAsync()
    {
        try
        {
            await CloseCurrentNotebookTabAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"关闭标签前保存失败。\n\n{exception.Message}", "关闭失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CloseCurrentNotebookTabShortcut()
    {
        await CloseCurrentNotebookTabSafelyAsync();
    }

    private void CycleNotebookTabs(int offset)
    {
        if (_openNotebookTabs.Count < 2 || string.IsNullOrWhiteSpace(_currentNotebookPath)) return;
        var index = _openNotebookTabs.FindIndex(tab => string.Equals(tab.FilePath, _currentNotebookPath, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = 0;
        var target = (index + offset) % _openNotebookTabs.Count;
        if (target < 0) target += _openNotebookTabs.Count;
        _ = OpenNotebookAsync(_openNotebookTabs[target].FilePath);
    }

    private readonly List<Guid> _pageBackHistory = [];
    private readonly List<Guid> _pageForwardHistory = [];
    private bool _isNavigatingPageHistory;

    private void ResetPageVisitHistory()
    {
        _pageBackHistory.Clear();
        _pageForwardHistory.Clear();
        UpdatePageVisitHistoryButtons();
    }

    private void RecordPageVisit(Guid fromPageId, Guid toPageId)
    {
        if (_isNavigatingPageHistory || fromPageId == Guid.Empty || fromPageId == toPageId) return;
        if (_pageBackHistory.Count == 0 || _pageBackHistory[^1] != fromPageId) _pageBackHistory.Add(fromPageId);
        if (_pageBackHistory.Count > 100) _pageBackHistory.RemoveAt(0);
        _pageForwardHistory.Clear();
        UpdatePageVisitHistoryButtons();
    }

    private void NavigatePageHistory(bool goBack)
    {
        if (_currentNotebook is null || _currentPage is null) return;
        var source = goBack ? _pageBackHistory : _pageForwardHistory;
        var destination = goBack ? _pageForwardHistory : _pageBackHistory;
        while (source.Count > 0)
        {
            var targetId = source[^1];
            source.RemoveAt(source.Count - 1);
            var targetIndex = _currentNotebook.Pages.FindIndex(page => page.Id == targetId);
            if (targetIndex < 0 || targetId == _currentPage.Id) continue;
            destination.Add(_currentPage.Id);
            _isNavigatingPageHistory = true;
            try { NavigateToPage(targetIndex + 1); }
            finally { _isNavigatingPageHistory = false; }
            UpdatePageVisitHistoryButtons();
            return;
        }
        UpdatePageVisitHistoryButtons();
        StatusText.Text = goBack ? "没有更早访问的页面" : "没有更晚访问的页面";
    }

    private void PageHistoryBack_Click(object sender, RoutedEventArgs e) => NavigatePageHistory(true);
    private void PageHistoryForward_Click(object sender, RoutedEventArgs e) => NavigatePageHistory(false);

    private void UpdatePageVisitHistoryButtons()
    {
        if (PageHistoryBackButton is null || PageHistoryForwardButton is null) return;
        PageHistoryBackButton.IsEnabled = _pageBackHistory.Count > 0;
        PageHistoryForwardButton.IsEnabled = _pageForwardHistory.Count > 0;
    }
}
