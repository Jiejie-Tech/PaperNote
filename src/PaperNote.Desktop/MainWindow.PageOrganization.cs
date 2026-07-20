using System.Windows;
using System.Windows.Controls;
using PaperNote.Desktop.Models;
using PaperNote.Desktop.Services;
using PaperNote.Desktop.ViewModels;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private void ResetPageFilterControls()
    {
        PageSearchBox.Text = string.Empty;
        BookmarkedPagesOnlyToggle.IsChecked = false;
    }

    private bool IsPageFilterActive()
    {
        return BookmarkedPagesOnlyToggle.IsChecked == true || !string.IsNullOrWhiteSpace(PageSearchBox.Text);
    }

    private bool MatchesPageFilter(NotebookPage page, int index)
    {
        return MatchesPageQuery(page, index, PageSearchBox.Text.Trim(), BookmarkedPagesOnlyToggle.IsChecked == true);
    }

    private void PageSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || _isSwitchingPage || _currentNotebook is null) return;
        RefreshPageItems(_currentPage?.Id);
        UpdatePageFilterStatus();
    }

    private void BookmarkedPagesOnlyToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || _isSwitchingPage || _currentNotebook is null) return;
        RefreshPageItems(_currentPage?.Id);
        UpdatePageFilterStatus();
    }

    private void UpdatePageFilterStatus()
    {
        if (_currentNotebook is null) return;
        if (!IsPageFilterActive())
        {
            StatusText.Text = $"共 {_currentNotebook.Pages.Count} 页";
            return;
        }

        StatusText.Text = _pageItems.Count == 0
            ? "没有找到符合条件的页面"
            : $"找到 {_pageItems.Count} 页 · 可按 Ctrl 或 Shift 多选";
    }

    private void PageTitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || _isSwitchingPage || _currentPage is null || _isReadOnly) return;
        var title = NotebookStorageService.NormalizePageTitle(PageTitleBox.Text);
        if (_currentPage.Title == title) return;
        _currentPage.Title = title;
        _currentPage.ModifiedAt = DateTimeOffset.Now;
        UpdatePageItemMetadata(_currentPage);
        RefreshOutlineIfVisible();
        MarkDirty();
    }

    private void TogglePageBookmark_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_currentNotebook is null || _isReadOnly || sender is not Button { CommandParameter: PageItemViewModel item }) return;
        var page = _currentNotebook.Pages.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (page is null) return;
        SetPageBookmarked(page, !page.IsBookmarked);
    }

    private void CurrentPageBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage is null || _isReadOnly) return;
        SetPageBookmarked(_currentPage, !_currentPage.IsBookmarked);
    }

    private void SetPageBookmarked(NotebookPage page, bool isBookmarked)
    {
        if (page.IsBookmarked == isBookmarked) return;
        page.IsBookmarked = isBookmarked;
        page.ModifiedAt = DateTimeOffset.Now;
        UpdatePageItemMetadata(page);
        if (_currentPage?.Id == page.Id) UpdateCurrentPageMetadataControls(page);
        var selectedIds = PageListBox.SelectedItems.OfType<PageItemViewModel>().Select(item => item.Id).ToHashSet();
        if (BookmarkedPagesOnlyToggle.IsChecked == true) RefreshPageItems(_currentPage?.Id, selectedIds);
        else if (PageOverviewOverlay.Visibility == Visibility.Visible) RefreshPageOverviewItems(_currentPage?.Id, selectedIds);
        MarkDirty();
        StatusText.Text = isBookmarked ? "已添加页面书签" : "已取消页面书签";
    }

    private bool SetPagesBookmarked(IReadOnlySet<Guid> pageIds, bool isBookmarked)
    {
        if (_currentNotebook is null || pageIds.Count == 0 || _isReadOnly) return false;
        var affected = _currentNotebook.Pages.Where(page => pageIds.Contains(page.Id) && page.IsBookmarked != isBookmarked).ToArray();
        if (affected.Length == 0) return false;
        foreach (var page in affected)
        {
            page.IsBookmarked = isBookmarked;
            page.ModifiedAt = DateTimeOffset.Now;
            UpdatePageItemMetadata(page);
        }
        if (_currentPage is not null) UpdateCurrentPageMetadataControls(_currentPage);
        RefreshPageItems(_currentPage?.Id, pageIds);
        MarkDirty();
        StatusText.Text = isBookmarked ? $"已为 {affected.Length} 页添加书签" : $"已取消 {affected.Length} 页的书签";
        return true;
    }

    private void UpdateCurrentPageMetadataControls(NotebookPage page)
    {
        PageTitleBox.Text = page.Title;
        CurrentPageBookmarkButton.Content = page.IsBookmarked ? "★ 已收藏" : "☆ 加书签";
        CurrentPageBookmarkButton.ToolTip = page.IsBookmarked ? "取消当前页书签" : "为当前页添加书签";
        UpdateCurrentPageOutlineButton(page);
    }

    private void UpdatePageItemMetadata(NotebookPage page)
    {
        var item = _pageItems.FirstOrDefault(candidate => candidate.Id == page.Id);
        if (item is null) return;
        item.Title = page.Title;
        item.IsBookmarked = page.IsBookmarked;
        var overviewItem = _overviewPageItems.FirstOrDefault(candidate => candidate.Id == page.Id);
        if (overviewItem is not null)
        {
            overviewItem.Title = page.Title;
            overviewItem.IsBookmarked = page.IsBookmarked;
        }
    }
}
