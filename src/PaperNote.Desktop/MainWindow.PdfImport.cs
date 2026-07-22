using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Desktop.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private string _pendingPdfImportPath = string.Empty;
    private int _pendingPdfImportPageCount;
    private CancellationTokenSource? _pdfImportCancellation;
    private bool _pdfImportBusy;

    private async void ImportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null || _isReadOnly || _pdfImportBusy) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择要导入的 PDF",
            Filter = "PDF 文件|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        ImportPdfButton.IsEnabled = false;
        StatusText.Text = "正在读取 PDF 信息…";
        try
        {
            var pageCount = await Task.Run(() => PdfImportService.GetPageCount(dialog.FileName));
            ShowPdfImportOptions(dialog.FileName, pageCount);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            StatusText.Text = "PDF 读取失败";
            MessageBox.Show(this, exception.Message, "无法读取 PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportPdfButton.IsEnabled = !_isReadOnly && !_pdfImportBusy;
        }
    }

    private void ShowPdfImportOptions(string filePath, int pageCount)
    {
        _pendingPdfImportPath = filePath;
        _pendingPdfImportPageCount = pageCount;
        PdfImportFileText.Text = Path.GetFileName(filePath);
        PdfImportPageCountText.Text = $"共 {pageCount} 页 · 单次最多导入 {PdfImportService.MaximumPageCount} 页 · 支持取消后续接";
        PdfImportRangeBox.Text = PdfPageRangeService.DefaultSelection(pageCount);
        SelectComboBoxItem(PdfImportPlacementCombo, "AfterCurrent");
        SetPdfImportBusy(false);
        PdfImportOptionsOverlay.Visibility = Visibility.Visible;
        UpdatePdfImportSelectionSummary();
        PdfImportRangeBox.Focus();
        PdfImportRangeBox.SelectAll();
    }

    private void PdfImportRangeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || _pdfImportBusy || PdfImportOptionsOverlay.Visibility != Visibility.Visible) return;
        UpdatePdfImportSelectionSummary();
    }

    private void PdfImportPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfImportBusy || sender is not Button { Tag: string preset } || _pendingPdfImportPageCount <= 0) return;
        PdfImportRangeBox.Text = preset switch
        {
            "Odd" => string.Join(",", Enumerable.Range(1, _pendingPdfImportPageCount).Where(page => page % 2 == 1).Take(PdfImportService.MaximumPageCount)),
            "Even" => string.Join(",", Enumerable.Range(1, _pendingPdfImportPageCount).Where(page => page % 2 == 0).Take(PdfImportService.MaximumPageCount)),
            _ => PdfPageRangeService.DefaultSelection(_pendingPdfImportPageCount)
        };
    }

    private void UpdatePdfImportSelectionSummary()
    {
        try
        {
            var pages = PdfImportService.ParsePageSelection(PdfImportRangeBox.Text, _pendingPdfImportPageCount);
            PdfImportRangeHintText.Text = $"将导入 {pages.Count} 页 · {SummarizePageSelection(pages)}";
            PdfImportRangeHintText.Foreground = new SolidColorBrush(PageThumbnailService.ParsePaperColor("#315CBB"));
            PdfImportContinueButton.IsEnabled = !_pdfImportBusy;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            PdfImportRangeHintText.Text = exception.Message;
            PdfImportRangeHintText.Foreground = new SolidColorBrush(PageThumbnailService.ParsePaperColor("#C24152"));
            PdfImportContinueButton.IsEnabled = false;
        }
    }

    private static string SummarizePageSelection(IReadOnlyList<int> pages)
    {
        if (pages.Count <= 8) return $"页码 {string.Join("、", pages)}";
        return $"页码 {string.Join("、", pages.Take(4))} … {string.Join("、", pages.TakeLast(2))}";
    }

    private void CancelPdfImportOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfImportBusy)
        {
            PdfImportCancelButton.IsEnabled = false;
            PdfImportCancelButton.Content = "正在取消…";
            PdfImportProgressText.Text = "正在安全停止；已经完成的页面会保留，下次可继续。";
            _pdfImportCancellation?.Cancel();
            return;
        }
        ClosePdfImportOptions();
    }

    private void ClosePdfImportOptions()
    {
        if (_pdfImportBusy)
        {
            _pdfImportCancellation?.Cancel();
            return;
        }
        PdfImportOptionsOverlay.Visibility = Visibility.Collapsed;
        _pendingPdfImportPath = string.Empty;
        _pendingPdfImportPageCount = 0;
        PdfImportProgressPanel.Visibility = Visibility.Collapsed;
        InkSurface.Focus();
    }

    private async void ContinuePdfImport_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null || _isReadOnly || _pdfImportBusy || string.IsNullOrWhiteSpace(_pendingPdfImportPath)) return;
        IReadOnlyList<int> pageNumbers;
        try
        {
            pageNumbers = PdfImportService.ParsePageSelection(PdfImportRangeBox.Text, _pendingPdfImportPageCount);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            PdfImportRangeHintText.Text = exception.Message;
            return;
        }

        var filePath = _pendingPdfImportPath;
        var placement = PdfImportPlacementCombo.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : "AfterCurrent";
        _pdfImportCancellation?.Dispose();
        _pdfImportCancellation = new CancellationTokenSource();
        var token = _pdfImportCancellation.Token;
        SetPdfImportBusy(true);
        ImportPdfButton.IsEnabled = false;
        StatusText.Text = $"正在导入 PDF… 0/{pageNumbers.Count} 页";
        var progress = new Progress<PdfImportProgress>(item =>
        {
            PdfImportProgressBar.Value = item.Fraction;
            if (token.IsCancellationRequested)
            {
                PdfImportProgressText.Text = $"正在安全取消… · 已完成 {item.CompletedPages}/{item.TotalPages}";
                PdfImportCancelButton.IsEnabled = false;
                PdfImportCancelButton.Content = "正在取消…";
                return;
            }

            PdfImportProgressText.Text = $"{item.Message} · {item.CompletedPages}/{item.TotalPages}";
            StatusText.Text = $"正在导入 PDF… {item.CompletedPages}/{item.TotalPages} 页";
        });

        try
        {
            var importedPages = await PdfImportService.ImportAsync(
                filePath,
                pageNumbers,
                cacheDirectory: PdfImportService.GetDefaultCacheDirectory(),
                progress: progress,
                cancellationToken: token);
            InsertImportedPdfPages(importedPages, Path.GetFileName(filePath), placement);
            ClosePdfImportOptionsAfterCompletion();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "PDF 导入已取消 · 下次会从缓存续接";
            PdfImportRangeHintText.Text = "已安全取消。重新点击“开始导入”会复用已完成页面。";
            PdfImportRangeHintText.Foreground = new SolidColorBrush(PageThumbnailService.ParsePaperColor("#315CBB"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            StatusText.Text = "PDF 导入失败 · 可重试续接";
            PdfImportRangeHintText.Text = $"{exception.Message} 已完成页面已保留，可修复问题后重试。";
            PdfImportRangeHintText.Foreground = new SolidColorBrush(PageThumbnailService.ParsePaperColor("#C24152"));
            MessageBox.Show(this, PdfImportRangeHintText.Text, "无法导入 PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetPdfImportBusy(false);
            _pdfImportCancellation?.Dispose();
            _pdfImportCancellation = null;
            ImportPdfButton.IsEnabled = !_isReadOnly;
            if (PdfImportOptionsOverlay.Visibility == Visibility.Visible) UpdatePdfImportSelectionSummary();
        }
    }

    private void SetPdfImportBusy(bool value)
    {
        _pdfImportBusy = value;
        PdfImportRangeBox.IsEnabled = !value;
        PdfImportPlacementCombo.IsEnabled = !value;
        PdfImportContinueButton.IsEnabled = !value;
        PdfImportContinueButton.Content = value ? "正在导入…" : "开始导入";
        PdfImportCancelButton.IsEnabled = true;
        PdfImportCancelButton.Content = value ? "取消导入" : "取消";
        PdfImportProgressPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (value)
        {
            PdfImportProgressBar.Value = 0;
            PdfImportProgressText.Text = "正在准备缓存和源文件…";
        }
    }

    private void ClosePdfImportOptionsAfterCompletion()
    {
        _pdfImportBusy = false;
        PdfImportOptionsOverlay.Visibility = Visibility.Collapsed;
        _pendingPdfImportPath = string.Empty;
        _pendingPdfImportPageCount = 0;
        PdfImportProgressPanel.Visibility = Visibility.Collapsed;
        InkSurface.Focus();
    }

    private void InsertImportedPdfPages(IReadOnlyList<ImportedPdfPage> importedPages, string sourceName) =>
        InsertImportedPdfPages(importedPages, sourceName, "AfterCurrent");

    private void InsertImportedPdfPages(IReadOnlyList<ImportedPdfPage> importedPages, string sourceName, string placement)
    {
        if (_currentNotebook is null || importedPages.Count == 0 || _isReadOnly) return;
        CaptureCurrentPage();
        var currentIndex = _currentPage is null ? _currentNotebook.Pages.Count - 1 : _currentNotebook.Pages.IndexOf(_currentPage);
        var insertIndex = placement switch
        {
            "BeforeCurrent" => Math.Clamp(currentIndex, 0, _currentNotebook.Pages.Count),
            "End" => _currentNotebook.Pages.Count,
            _ => Math.Clamp(currentIndex + 1, 0, _currentNotebook.Pages.Count)
        };
        var newPages = importedPages.Select(imported => new NotebookPage
        {
            Title = $"PDF 第 {imported.PageNumber} 页",
            PaperTemplate = "Blank",
            PaperColor = "#FFFFFF",
            BackgroundImageData = imported.ImageData,
            BackgroundSourceType = "PDF",
            BackgroundSourceName = sourceName,
            BackgroundPageNumber = imported.PageNumber
        }).ToArray();

        _currentNotebook.Pages.InsertRange(insertIndex, newPages);
        var firstPage = newPages[0];
        _currentNotebook.CurrentPageId = firstPage.Id;
        _currentPage = firstPage;
        RefreshPageItems(firstPage.Id);
        LoadPage(firstPage);
        RefreshOutlineIfVisible();
        MarkDirty();
        var restored = importedPages.Count(page => page.FromCache);
        StatusText.Text = restored > 0
            ? $"已导入 {newPages.Length} 页 PDF · 从缓存续接 {restored} 页"
            : $"已导入 {newPages.Length} 页 PDF · 插入到第 {insertIndex + 1} 页起";
    }
}
