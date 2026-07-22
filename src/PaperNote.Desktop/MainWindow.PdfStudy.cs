using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PaperNote.Core.Models;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<PdfStudySearchItem> _pdfStudySearchResults = [];
    private readonly ObservableCollection<PdfStudyOutlineItem> _pdfStudyOutlineItems = [];
    private readonly ObservableCollection<PdfStudyLinkItem> _pdfStudyLinkItems = [];
    private readonly ObservableCollection<PdfStudyAnnotationItem> _pdfStudyAnnotationItems = [];

    private sealed record PdfStudySearchItem(Guid PageId, int PageNumber, string Source, string Snippet, string Title)
    {
        public string PageText => $"第 {PageNumber} 页";
    }

    private sealed record PdfStudyOutlineItem(Guid TargetPageId, int PageNumber, int Level, string Title, bool IsImported)
    {
        public string LevelText => IsImported ? $"PDF · {Level} 级" : $"{Level} 级";
        public string Display => $"{Title} · 第 {PageNumber} 页";
        public Thickness Indent => new(Math.Max(0, Level - 1) * 18, 2, 0, 2);
    }

    private sealed record PdfStudyLinkItem(Guid? TargetPageId, int TargetSourcePageNumber, string Label, string Detail);

    private sealed record PdfStudyAnnotationItem(PageAnnotationSummary Summary)
    {
        public string KindText => Summary.Kind switch
        {
            PageAnnotationKind.Comment => "文字评论",
            PageAnnotationKind.Highlighter => "荧光笔",
            PageAnnotationKind.Pen => "钢笔",
            PageAnnotationKind.Text => "文字",
            PageAnnotationKind.Image => "图片",
            PageAnnotationKind.Shape => "形状",
            _ => Summary.Kind.ToString()
        };
        public string Preview => string.IsNullOrWhiteSpace(Summary.Preview) ? "（无文字说明）" : Summary.Preview;
        public string PageText => $"第 {Summary.PageNumber} 页";
    }

    private void InitializePdfStudy()
    {
        PdfStudyResultsList.ItemsSource = _pdfStudySearchResults;
        PdfStudyOutlineList.ItemsSource = _pdfStudyOutlineItems;
        PdfStudyLinksList.ItemsSource = _pdfStudyLinkItems;
        PdfStudyAnnotationList.ItemsSource = _pdfStudyAnnotationItems;
        PdfStudyAnnotationKind.SelectedIndex = 0;
        PdfStudyAnnotationColor.SelectedIndex = 0;
        PdfStudyCommentColor.SelectedIndex = 0;
    }

    private void OpenPdfStudy_Click(object sender, RoutedEventArgs e) => OpenPdfStudy();

    private void OpenPdfStudy()
    {
        if (_currentNotebook is null) return;
        CaptureCurrentPage();
        PdfDocumentContentService.ResolveInternalLinks(_currentNotebook);
        PdfStudyOverlay.Visibility = Visibility.Visible;
        RefreshPdfStudyAll();
        PdfStudySearchBox.Focus();
        StatusText.Text = "PDF 学习中心已打开 · 所有内容仅在本地处理";
    }

    private void ClosePdfStudy_Click(object sender, RoutedEventArgs e) => ClosePdfStudy();

    private void ClosePdfStudy()
    {
        PdfStudyOverlay.Visibility = Visibility.Collapsed;
        InkSurface.Focus();
        UpdatePageNavigationStatus();
    }

    private void RefreshPdfStudyAll()
    {
        RefreshPdfStudySearch();
        RefreshPdfStudyOutline();
        RefreshPdfStudyLinks();
        RefreshPdfStudyAnnotations();
        UpdatePdfStudyStatus();
    }

    private void PdfStudySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || PdfStudyOverlay.Visibility != Visibility.Visible) return;
        RefreshPdfStudySearch();
    }

    private void PdfStudySearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        JumpToPdfStudySearchResult();
        e.Handled = true;
    }

    private void RefreshPdfStudySearch()
    {
        _pdfStudySearchResults.Clear();
        if (_currentNotebook is null) return;
        var query = PdfStudySearchBox.Text.Trim();
        if (query.Length == 0)
        {
            UpdatePdfStudyStatus();
            return;
        }

        var search = new OfflineSearchService();
        search.Index(_currentNotebook);
        foreach (var hit in search.Search(query, 300).Where(hit => hit.PageId != Guid.Empty))
        {
            var page = _currentNotebook.Pages.FirstOrDefault(item => item.Id == hit.PageId);
            if (page is null) continue;
            var title = string.IsNullOrWhiteSpace(page.Title)
                ? page.BackgroundSourceType == "PDF" && page.BackgroundPageNumber > 0 ? $"PDF 原第 {page.BackgroundPageNumber} 页" : $"第 {hit.PageNumber} 页"
                : page.Title.Trim();
            _pdfStudySearchResults.Add(new PdfStudySearchItem(hit.PageId, hit.PageNumber, hit.Source, hit.Snippet, title));
        }
        PdfStudyResultsList.SelectedItem = _pdfStudySearchResults.FirstOrDefault();
        UpdatePdfStudyStatus();
    }

    private void RefreshPdfStudyOutline()
    {
        _pdfStudyOutlineItems.Clear();
        if (_currentNotebook is null) return;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _currentNotebook.Pages.Count; index++)
        {
            var page = _currentNotebook.Pages[index];
            if (page.OutlineLevel <= 0) continue;
            var title = string.IsNullOrWhiteSpace(page.Title) ? $"第 {index + 1} 页" : page.Title.Trim();
            keys.Add($"{page.Id:N}|{page.OutlineLevel}|{title}");
            _pdfStudyOutlineItems.Add(new PdfStudyOutlineItem(page.Id, index + 1, page.OutlineLevel, title, false));
        }
        foreach (var entry in _currentNotebook.OutlineEntries)
        {
            if (!entry.TargetPageId.HasValue) continue;
            var index = _currentNotebook.Pages.FindIndex(page => page.Id == entry.TargetPageId.Value);
            if (index < 0) continue;
            var title = string.IsNullOrWhiteSpace(entry.Title) ? $"第 {index + 1} 页" : entry.Title.Trim();
            var level = Math.Clamp(entry.Level, 1, 6);
            if (!keys.Add($"{entry.TargetPageId.Value:N}|{level}|{title}")) continue;
            _pdfStudyOutlineItems.Add(new PdfStudyOutlineItem(entry.TargetPageId.Value, index + 1, level, title, entry.IsImported));
        }
        var sorted = _pdfStudyOutlineItems.OrderBy(item => item.PageNumber).ThenBy(item => item.Level).ToArray();
        _pdfStudyOutlineItems.Clear();
        foreach (var item in sorted) _pdfStudyOutlineItems.Add(item);
        PdfStudyOutlineList.SelectedItem = _pdfStudyOutlineItems.FirstOrDefault(item => item.TargetPageId == _currentPage?.Id) ?? _pdfStudyOutlineItems.FirstOrDefault();
    }

    private void RefreshPdfStudyLinks()
    {
        _pdfStudyLinkItems.Clear();
        if (_currentNotebook is null || _currentPage is null) return;
        PdfDocumentContentService.ResolveInternalLinks(_currentNotebook);
        foreach (var link in _currentPage.PdfLinks)
        {
            var targetIndex = link.TargetPageId.HasValue ? _currentNotebook.Pages.FindIndex(page => page.Id == link.TargetPageId.Value) : -1;
            var label = string.IsNullOrWhiteSpace(link.Label) ? $"前往 PDF 原第 {link.TargetSourcePageNumber} 页" : link.Label.Trim();
            var detail = targetIndex >= 0 ? $"已映射到笔记第 {targetIndex + 1} 页" : $"目标原页 {link.TargetSourcePageNumber} 尚未导入";
            _pdfStudyLinkItems.Add(new PdfStudyLinkItem(link.TargetPageId, link.TargetSourcePageNumber, label, detail));
        }
        PdfStudyLinksList.SelectedItem = _pdfStudyLinkItems.FirstOrDefault();
    }

    private void PdfStudyAnnotationFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || PdfStudyOverlay.Visibility != Visibility.Visible) return;
        RefreshPdfStudyAnnotations();
    }

    private void RefreshPdfStudyAnnotations()
    {
        _pdfStudyAnnotationItems.Clear();
        if (_currentNotebook is null) return;
        var kindTag = (PdfStudyAnnotationKind.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        PageAnnotationKind? kind = Enum.TryParse<PageAnnotationKind>(kindTag, out var parsed) ? parsed : null;
        var color = (PdfStudyAnnotationColor.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        foreach (var summary in PageAnnotationService.Build(_currentNotebook, kind, color))
            _pdfStudyAnnotationItems.Add(new PdfStudyAnnotationItem(summary));
        PdfStudyAnnotationList.SelectedItem = _pdfStudyAnnotationItems.FirstOrDefault(item => item.Summary.PageId == _currentPage?.Id) ?? _pdfStudyAnnotationItems.FirstOrDefault();
        UpdatePdfStudyStatus();
    }

    private void PdfStudyAddComment_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage is null || _isReadOnly) return;
        var text = PdfStudyCommentBox.Text.Trim();
        if (text.Length == 0)
        {
            StatusText.Text = "请先输入评论内容";
            PdfStudyCommentBox.Focus();
            return;
        }
        var color = (PdfStudyCommentColor.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "#F0B429";
        PageAnnotationService.AddComment(_currentPage, text, color);
        PdfStudyCommentBox.Clear();
        MarkDirty();
        RefreshPdfStudySearch();
        RefreshPdfStudyAnnotations();
        StatusText.Text = "已在当前页添加本地文字评论";
    }

    private void PdfStudyDeleteComment_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null || _isReadOnly || PdfStudyAnnotationList.SelectedItem is not PdfStudyAnnotationItem selected) return;
        if (selected.Summary.Kind != PageAnnotationKind.Comment)
        {
            StatusText.Text = "钢笔、荧光笔和页面对象请回到画布中编辑；这里只能删除文字评论";
            return;
        }
        var page = _currentNotebook.Pages.FirstOrDefault(item => item.Id == selected.Summary.PageId);
        if (page is null || !PageAnnotationService.DeleteComment(page, selected.Summary.Id)) return;
        MarkDirty();
        RefreshPdfStudySearch();
        RefreshPdfStudyAnnotations();
        StatusText.Text = "已删除文字评论";
    }

    private void PdfStudyResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => JumpToPdfStudySearchResult();
    private void PdfStudyJump_Click(object sender, RoutedEventArgs e) => JumpToPdfStudySearchResult();
    private void PdfStudyOutlineList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => JumpToPdfStudyOutline();
    private void PdfStudyOutlineJump_Click(object sender, RoutedEventArgs e) => JumpToPdfStudyOutline();
    private void PdfStudyLinksList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => JumpToPdfStudyLink();
    private void PdfStudyLinkJump_Click(object sender, RoutedEventArgs e) => JumpToPdfStudyLink();
    private void PdfStudyAnnotationList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => JumpToPdfStudyAnnotation();
    private void PdfStudyAnnotationJump_Click(object sender, RoutedEventArgs e) => JumpToPdfStudyAnnotation();

    private bool JumpToPdfStudySearchResult() => PdfStudyResultsList.SelectedItem is PdfStudySearchItem item && JumpFromPdfStudy(item.PageNumber, $"已打开第 {item.PageNumber} 页搜索结果");
    private bool JumpToPdfStudyOutline() => PdfStudyOutlineList.SelectedItem is PdfStudyOutlineItem item && JumpFromPdfStudy(item.PageNumber, $"已打开目录项：{item.Title}");
    private bool JumpToPdfStudyAnnotation() => PdfStudyAnnotationList.SelectedItem is PdfStudyAnnotationItem item && JumpFromPdfStudy(item.Summary.PageNumber, $"已打开第 {item.Summary.PageNumber} 页批注");

    private bool JumpToPdfStudyLink()
    {
        if (_currentNotebook is null || PdfStudyLinksList.SelectedItem is not PdfStudyLinkItem item) return false;
        if (!item.TargetPageId.HasValue)
        {
            StatusText.Text = $"PDF 原第 {item.TargetSourcePageNumber} 页尚未导入，暂时无法跳转";
            return false;
        }
        var index = _currentNotebook.Pages.FindIndex(page => page.Id == item.TargetPageId.Value);
        return index >= 0 && JumpFromPdfStudy(index + 1, $"已沿 PDF 内部链接跳转到第 {index + 1} 页");
    }

    private bool JumpFromPdfStudy(int pageNumber, string status)
    {
        if (!NavigateToPage(pageNumber)) return false;
        ClosePdfStudy();
        StatusText.Text = status;
        return true;
    }

    private void UpdatePdfStudyStatus()
    {
        if (PdfStudyStatusText is null || _currentNotebook is null) return;
        var searchablePages = _currentNotebook.Pages.Count(page => !string.IsNullOrWhiteSpace(page.PdfText));
        var comments = _currentNotebook.Pages.Sum(page => page.Comments.Count);
        var links = _currentNotebook.Pages.Sum(page => page.PdfLinks.Count);
        PdfStudyStatusText.Text = $"{searchablePages} 页含 PDF 文本 · {_pdfStudySearchResults.Count} 个搜索结果 · {_pdfStudyOutlineItems.Count} 个目录项 · {links} 个内部链接 · {comments} 条评论";
    }

    private void RefreshPdfStudyIfVisible()
    {
        if (PdfStudyOverlay.Visibility == Visibility.Visible) RefreshPdfStudyAll();
    }
}
