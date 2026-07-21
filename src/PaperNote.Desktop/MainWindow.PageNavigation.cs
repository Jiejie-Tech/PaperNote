using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PaperNote.Core.Models;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;
using PaperNote.Desktop.ViewModels;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<PageItemViewModel> _overviewPageItems = [];
    private readonly Dictionary<ThumbnailCacheKey, ThumbnailCacheEntry> _pageThumbnailCache = [];

    private readonly record struct ThumbnailCacheKey(Guid PageId, int Width, int Height);
    private sealed record ThumbnailCacheEntry(DateTimeOffset ModifiedAt, ImageSource Image);

    private void InitializePageNavigation()
    {
        PageOverviewListBox.ItemsSource = _overviewPageItems;
        UpdatePageNavigationStatus();
    }

    private ImageSource GetPageThumbnail(NotebookPage page, int width = 150, int height = 212)
    {
        var key = new ThumbnailCacheKey(page.Id, width, height);
        if (_pageThumbnailCache.TryGetValue(key, out var cached) && cached.ModifiedAt == page.ModifiedAt)
        {
            return cached.Image;
        }

        var image = PageThumbnailService.CreatePageBitmap(page, width, height);
        _pageThumbnailCache[key] = new ThumbnailCacheEntry(page.ModifiedAt, image);
        return image;
    }

    private void InvalidatePageThumbnail(Guid pageId)
    {
        foreach (var key in _pageThumbnailCache.Keys.Where(key => key.PageId == pageId).ToArray())
        {
            _pageThumbnailCache.Remove(key);
        }
    }

    private void ClearPageThumbnailCache() => _pageThumbnailCache.Clear();

    private void RefreshPageOverviewItems(Guid? selectedId = null, IReadOnlySet<Guid>? selectedIds = null)
    {
        if (_currentNotebook is null)
        {
            _overviewPageItems.Clear();
            UpdateOverviewStatus();
            return;
        }

        var query = PageOverviewSearchBox.Text.Trim();
        var bookmarksOnly = PageOverviewBookmarkedOnlyToggle.IsChecked == true;
        var previousIds = selectedIds ?? PageOverviewListBox.SelectedItems.OfType<PageItemViewModel>().Select(item => item.Id).ToHashSet();
        var preferredId = selectedId ?? _currentPage?.Id ?? _currentNotebook.CurrentPageId;

        _overviewPageItems.Clear();
        for (var index = 0; index < _currentNotebook.Pages.Count; index++)
        {
            var page = _currentNotebook.Pages[index];
            if (!MatchesPageQuery(page, index, query, bookmarksOnly)) continue;
            _overviewPageItems.Add(new PageItemViewModel
            {
                Id = page.Id,
                Number = index + 1,
                Title = page.Title,
                IsBookmarked = page.IsBookmarked,
                Thumbnail = GetPageThumbnail(page, 180, 255)
            });
        }

        PageOverviewListBox.SelectedItems.Clear();
        foreach (var item in _overviewPageItems.Where(item => previousIds.Contains(item.Id)))
        {
            PageOverviewListBox.SelectedItems.Add(item);
        }

        var preferred = _overviewPageItems.FirstOrDefault(item => item.Id == preferredId);
        if (PageOverviewListBox.SelectedItems.Count == 0 && preferred is not null)
        {
            PageOverviewListBox.SelectedItems.Add(preferred);
        }
        if (preferred is not null) PageOverviewListBox.ScrollIntoView(preferred);
        UpdateOverviewStatus();
    }

    private static bool MatchesPageQuery(NotebookPage page, int index, string query, bool bookmarksOnly)
    {
        if (bookmarksOnly && !page.IsBookmarked) return false;
        if (query.Length == 0) return true;
        if ((index + 1).ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (page.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        return page.Objects.Any(item => item.Kind == "Text" && item.Text.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void OpenPageOverview_Click(object sender, RoutedEventArgs e) => OpenPageOverview();

    private void OpenPageOverview()
    {
        if (_currentNotebook is null) return;
        CaptureCurrentPage();
        PageOverviewOverlay.Visibility = Visibility.Visible;
        RefreshPageOverviewItems(_currentPage?.Id);
        PageOverviewSearchBox.Focus();
        StatusText.Text = $"页面总览 · 共 {_currentNotebook.Pages.Count} 页";
    }

    private void ClosePageOverview_Click(object sender, RoutedEventArgs e) => ClosePageOverview();

    private void ClosePageOverview()
    {
        PageOverviewOverlay.Visibility = Visibility.Collapsed;
        InkSurface.Focus();
        UpdatePageNavigationStatus();
    }

    private void PageOverviewSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || PageOverviewOverlay.Visibility != Visibility.Visible) return;
        RefreshPageOverviewItems(_currentPage?.Id);
    }

    private void PageOverviewBookmarkedOnlyToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || PageOverviewOverlay.Visibility != Visibility.Visible) return;
        RefreshPageOverviewItems(_currentPage?.Id);
    }

    private void PageOverviewListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOverviewStatus();

    private void UpdateOverviewStatus()
    {
        if (PageOverviewStatusText is null) return;
        var selectedCount = PageOverviewListBox?.SelectedItems.Count ?? 0;
        PageOverviewStatusText.Text = selectedCount > 0
            ? $"显示 {_overviewPageItems.Count} 页 · 已选择 {selectedCount} 页"
            : $"显示 {_overviewPageItems.Count} 页";
        if (OverviewJumpButton is not null) OverviewJumpButton.IsEnabled = selectedCount > 0;
    }

    private void PageOverviewListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PageOverviewListBox.SelectedItem is PageItemViewModel item) NavigateToPage(item.Number, true);
    }

    private void OverviewJumpToPage_Click(object sender, RoutedEventArgs e)
    {
        if (PageOverviewListBox.SelectedItem is PageItemViewModel item) NavigateToPage(item.Number, true);
    }

    private void OverviewAddBookmarks_Click(object sender, RoutedEventArgs e) => SetPagesBookmarked(GetSelectedPageIds(), true);
    private void OverviewRemoveBookmarks_Click(object sender, RoutedEventArgs e) => SetPagesBookmarked(GetSelectedPageIds(), false);

    private void PageJumpBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        NavigateFromPageJumpBox();
        e.Handled = true;
    }

    private void PageJumpBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdatePageNavigationStatus();

    private void JumpToPage_Click(object sender, RoutedEventArgs e) => NavigateFromPageJumpBox();

    private bool NavigateFromPageJumpBox()
    {
        if (_currentNotebook is null) return false;
        if (!int.TryParse(PageJumpBox.Text.Trim(), out var pageNumber))
        {
            StatusText.Text = "请输入有效页码";
            UpdatePageNavigationStatus();
            return false;
        }
        return NavigateToPage(pageNumber);
    }

    private bool NavigateToPage(int pageNumber, bool closeOverview = false)
    {
        if (_currentNotebook is null || pageNumber < 1 || pageNumber > _currentNotebook.Pages.Count)
        {
            StatusText.Text = _currentNotebook is null ? "当前没有打开笔记本" : $"页码范围为 1 至 {_currentNotebook.Pages.Count}";
            UpdatePageNavigationStatus();
            return false;
        }

        var page = _currentNotebook.Pages[pageNumber - 1];
        if (_currentPage?.Id != page.Id)
        {
            CaptureCurrentPage();
            if (_currentPage is not null) RecordPageVisit(_currentPage.Id, page.Id);
            if (IsPageFilterActive() && !MatchesPageFilter(page, pageNumber - 1)) ResetPageFilterControlsSilently();
            _currentPage = page;
            _currentNotebook.CurrentPageId = page.Id;
            RefreshPageItems(page.Id);
            LoadPage(page);
            MarkDirty();
        }
        else
        {
            RefreshPageItems(page.Id);
            UpdatePageNavigationStatus();
        }

        if (closeOverview) ClosePageOverview();
        StatusText.Text = $"已跳转到第 {pageNumber} 页 · {_activeToolDisplayName()} · {InkSurface.Strokes.Count} 条笔迹";
        return true;
    }

    private bool NavigateRelativePage(int offset)
    {
        if (_currentNotebook is null || _currentPage is null) return false;
        var currentIndex = _currentNotebook.Pages.IndexOf(_currentPage);
        var targetIndex = currentIndex + offset;
        if (targetIndex < 0 || targetIndex >= _currentNotebook.Pages.Count)
        {
            StatusText.Text = offset < 0 ? "已经是第一页" : "已经是最后一页";
            return false;
        }
        return NavigateToPage(targetIndex + 1);
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e) => NavigateRelativePage(-1);
    private void NextPage_Click(object sender, RoutedEventArgs e) => NavigateRelativePage(1);

    private void FocusPageJumpBox()
    {
        PageJumpBox.Focus();
        PageJumpBox.SelectAll();
    }

    private void UpdatePageNavigationStatus()
    {
        if (PageJumpBox is null || CurrentPageCountText is null) return;
        var total = _currentNotebook?.Pages.Count ?? 0;
        var current = _currentNotebook is null || _currentPage is null ? 0 : _currentNotebook.Pages.IndexOf(_currentPage) + 1;
        PageJumpBox.Text = current > 0 ? current.ToString() : string.Empty;
        CurrentPageCountText.Text = $"/ {total} 页";
        PreviousPageButton.IsEnabled = current > 1;
        NextPageButton.IsEnabled = current > 0 && current < total;
        UpdatePageVisitHistoryButtons();
    }

    private void ResetPageFilterControlsSilently()
    {
        var wasSwitching = _isSwitchingPage;
        _isSwitchingPage = true;
        try { ResetPageFilterControls(); }
        finally { _isSwitchingPage = wasSwitching; }
    }
}
