using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PaperNote.Core.Models;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private void BatchPageActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = CreateBatchPageActionsMenu(GetSelectedPageIds());
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu CreateBatchPageActionsMenu(IReadOnlySet<Guid> selectedIds)
    {
        var hasSelection = selectedIds.Count > 0;
        var canModify = !_isReadOnly && hasSelection;
        var selectedPdfCount = _currentNotebook?.Pages.Count(page => selectedIds.Contains(page.Id) && IsPdfBackgroundPage(page)) ?? 0;
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("复制所选页面", "", (_, _) => DuplicatePages(GetSelectedPageIds()), canModify));
        menu.Items.Add(CreateMenuItem("移至笔记开头", "", (_, _) => MovePagesToBoundary(GetSelectedPageIds(), true), canModify));
        menu.Items.Add(CreateMenuItem("移至笔记末尾", "", (_, _) => MovePagesToBoundary(GetSelectedPageIds(), false), canModify));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("按所选页面导出 PDF…", "", (_, _) => ExportSelectedPageSet(GetSelectedPageIds()), hasSelection));
        menu.Items.Add(CreateMenuItem("提取为新笔记本…", "", async (_, _) => await ExtractSelectedPagesAsync(GetSelectedPageIds()), hasSelection));
        menu.Items.Add(CreateMenuItem("删除所选页面", "", (_, _) => ConfirmDeleteSelectedPages(), canModify && _currentNotebook is { Pages.Count: > 1 }));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("为所选页面添加书签", "", (_, _) => SetPagesBookmarked(GetSelectedPageIds(), true), canModify));
        menu.Items.Add(CreateMenuItem("取消所选页面书签", "", (_, _) => SetPagesBookmarked(GetSelectedPageIds(), false), canModify));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("PDF 页面左转 90°", "", (_, _) => RotatePdfPages(GetSelectedPageIds(), -90), canModify && selectedPdfCount > 0));
        menu.Items.Add(CreateMenuItem("PDF 页面右转 90°", "", (_, _) => RotatePdfPages(GetSelectedPageIds(), 90), canModify && selectedPdfCount > 0));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("PDF 页面裁边 5%", "", (_, _) => SetPdfPagesCrop(GetSelectedPageIds(), 0.05), canModify && selectedPdfCount > 0));
        menu.Items.Add(CreateMenuItem("PDF 页面裁边 10%", "", (_, _) => SetPdfPagesCrop(GetSelectedPageIds(), 0.10), canModify && selectedPdfCount > 0));
        menu.Items.Add(CreateMenuItem("清除 PDF 页面裁剪", "", (_, _) => SetPdfPagesCrop(GetSelectedPageIds(), 0), canModify && selectedPdfCount > 0));
        menu.Items.Add(CreateMenuItem("恢复 PDF 原始方向和裁剪", "", (_, _) => ResetPdfPagesTransform(GetSelectedPageIds()), canModify && selectedPdfCount > 0));
        if (selectedPdfCount == 0) menu.Items.Add(new MenuItem { Header = "所选页面中没有 PDF 页面", IsEnabled = false });
        return menu;
    }

    private bool DuplicatePages(IReadOnlySet<Guid> pageIds)
    {
        if (_currentNotebook is null || pageIds.Count == 0 || _isReadOnly) return false;
        CaptureCurrentPage();
        var copies = PageBatchService.Duplicate(_currentNotebook, pageIds);
        if (copies.Count == 0) return false;
        var firstCopy = copies[0];
        var copyIds = copies.Select(page => page.Id).ToHashSet();
        _currentPage = firstCopy;
        _currentNotebook.CurrentPageId = firstCopy.Id;
        LoadPage(firstCopy);
        RefreshPageItems(firstCopy.Id, copyIds);
        MarkDirty();
        StatusText.Text = copies.Count == 1 ? "已复制 1 页" : $"已批量复制 {copies.Count} 页";
        return true;
    }

    private bool MovePagesToBoundary(IReadOnlySet<Guid> pageIds, bool toStart)
    {
        if (_currentNotebook is null || pageIds.Count == 0 || _isReadOnly) return false;
        CaptureCurrentPage();
        var moved = toStart ? PageBatchService.MoveToStart(_currentNotebook, pageIds) : PageBatchService.MoveToEnd(_currentNotebook, pageIds);
        if (!moved) return false;
        RefreshPageItems(_currentPage?.Id, pageIds);
        MarkDirty();
        StatusText.Text = toStart ? $"已将 {pageIds.Count} 页移至笔记开头" : $"已将 {pageIds.Count} 页移至笔记末尾";
        return true;
    }

    private void ExportSelectedPageSet(IReadOnlySet<Guid> pageIds)
    {
        if (_currentNotebook is null || pageIds.Count == 0) return;
        CaptureCurrentPage();
        var pages = _currentNotebook.Pages.Where(page => pageIds.Contains(page.Id)).ToArray();
        ShowPdfExportOptions(pages, pages.Length == 1 ? "所选页面" : $"所选 {pages.Length} 页");
    }

    private async Task ExtractSelectedPagesAsync(IReadOnlySet<Guid> pageIds)
    {
        if (_currentNotebook is null || pageIds.Count == 0) return;
        CaptureCurrentPage();
        var dialog = new SaveFileDialog
        {
            Title = "提取所选页面为新笔记本",
            Filter = "PaperNote 笔记本 (*.papernote)|*.papernote",
            FileName = $"{_currentNotebook.Title}-提取页面.papernote",
            AddExtension = true,
            DefaultExt = ".papernote"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var extracted = PageBatchService.ExtractDocument(_currentNotebook, pageIds, Path.GetFileNameWithoutExtension(dialog.FileName));
            await _notebookStorage.SaveAsync(extracted, dialog.FileName);
            StatusText.Text = $"已提取 {extracted.Pages.Count} 页 · {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            MessageBox.Show(this, $"无法提取页面。\n\n{exception.Message}", "提取失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfirmDeleteSelectedPages()
    {
        var ids = GetSelectedPageIds();
        if (_currentNotebook is null || ids.Count == 0 || _isReadOnly) return;
        if (ids.Count >= _currentNotebook.Pages.Count)
        {
            MessageBox.Show(this, "每个笔记本至少需要保留一页。请取消选择至少一页后再删除。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var result = MessageBox.Show(this, $"确定删除所选的 {ids.Count} 页吗？此操作无法撤销。", "批量删除页面", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes) DeletePages(ids);
    }

    private bool DeletePages(IReadOnlySet<Guid> pageIds)
    {
        if (_currentNotebook is null || pageIds.Count == 0 || _isReadOnly) return false;
        var existingIds = _currentNotebook.Pages.Where(page => pageIds.Contains(page.Id)).Select(page => page.Id).ToHashSet();
        if (existingIds.Count == 0 || existingIds.Count >= _currentNotebook.Pages.Count) return false;

        CaptureCurrentPage();
        var oldCurrentIndex = _currentPage is null ? 0 : _currentNotebook.Pages.IndexOf(_currentPage);
        _currentNotebook.Pages.RemoveAll(page => existingIds.Contains(page.Id));
        PageBatchService.CleanupNavigation(_currentNotebook);
        foreach (var pageId in existingIds) InvalidatePageThumbnail(pageId);
        if (_currentPage is null || existingIds.Contains(_currentPage.Id))
        {
            var nextIndex = Math.Clamp(oldCurrentIndex, 0, _currentNotebook.Pages.Count - 1);
            _currentPage = _currentNotebook.Pages[nextIndex];
            _currentNotebook.CurrentPageId = _currentPage.Id;
            LoadPage(_currentPage);
        }
        RefreshPageItems(_currentPage.Id);
        MarkDirty();
        StatusText.Text = $"已删除 {existingIds.Count} 页 · 剩余 {_currentNotebook.Pages.Count} 页";
        return true;
    }

    private bool RotatePdfPages(IReadOnlySet<Guid> pageIds, int delta)
    {
        return TransformPdfPages(pageIds, page => page.BackgroundRotation = ((page.BackgroundRotation + delta) % 360 + 360) % 360,
            count => $"已将 {count} 个 PDF 页面{(delta < 0 ? "左转" : "右转")} 90°");
    }

    private bool SetPdfPagesCrop(IReadOnlySet<Guid> pageIds, double amount)
    {
        amount = Math.Clamp(amount, 0, 0.2);
        return TransformPdfPages(pageIds, page =>
        {
            page.BackgroundCropLeft = amount;
            page.BackgroundCropTop = amount;
            page.BackgroundCropRight = amount;
            page.BackgroundCropBottom = amount;
        }, count => amount <= 0 ? $"已清除 {count} 个 PDF 页面的裁剪" : $"已将 {count} 个 PDF 页面裁边 {amount:P0}");
    }

    private bool ResetPdfPagesTransform(IReadOnlySet<Guid> pageIds)
    {
        return TransformPdfPages(pageIds, page =>
        {
            page.BackgroundRotation = 0;
            page.BackgroundCropLeft = page.BackgroundCropTop = page.BackgroundCropRight = page.BackgroundCropBottom = 0;
        }, count => $"已恢复 {count} 个 PDF 页面的原始方向和裁剪");
    }

    private bool TransformPdfPages(IReadOnlySet<Guid> pageIds, Action<NotebookPage> transform, Func<int, string> status)
    {
        if (_currentNotebook is null || pageIds.Count == 0 || _isReadOnly) return false;
        CaptureCurrentPage();
        var affected = _currentNotebook.Pages.Where(page => pageIds.Contains(page.Id) && IsPdfBackgroundPage(page)).ToArray();
        if (affected.Length == 0) return false;
        foreach (var page in affected)
        {
            transform(page);
            page.ModifiedAt = DateTimeOffset.Now;
        }
        if (_currentPage is not null && affected.Any(page => page.Id == _currentPage.Id))
        {
            PageBackgroundImage.Source = PageBackgroundService.CreateImageSource(_currentPage);
        }
        RefreshPageItems(_currentPage?.Id, pageIds);
        MarkDirty();
        StatusText.Text = status(affected.Length);
        return true;
    }

    private static bool IsPdfBackgroundPage(NotebookPage page)
    {
        return page.BackgroundSourceType == "PDF" && !string.IsNullOrWhiteSpace(page.BackgroundImageData);
    }
}
