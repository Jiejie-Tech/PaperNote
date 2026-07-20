using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PaperNote.Desktop.Models;
using PaperNote.Desktop.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private string _pendingPdfImportPath = string.Empty;
    private int _pendingPdfImportPageCount;

    private async void ImportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null || _isReadOnly) return;
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
            ImportPdfButton.IsEnabled = !_isReadOnly;
        }
    }

    private void ShowPdfImportOptions(string filePath, int pageCount)
    {
        _pendingPdfImportPath = filePath;
        _pendingPdfImportPageCount = pageCount;
        PdfImportFileText.Text = Path.GetFileName(filePath);
        PdfImportPageCountText.Text = $"共 {pageCount} 页 · 单次最多导入 {PdfImportService.MaximumPageCount} 页";
        PdfImportRangeBox.Text = pageCount <= PdfImportService.MaximumPageCount ? $"1-{pageCount}" : $"1-{PdfImportService.MaximumPageCount}";
        SelectComboBoxItem(PdfImportPlacementCombo, "AfterCurrent");
        PdfImportOptionsOverlay.Visibility = Visibility.Visible;
        UpdatePdfImportSelectionSummary();
        PdfImportRangeBox.Focus();
        PdfImportRangeBox.SelectAll();
    }

    private void PdfImportRangeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || PdfImportOptionsOverlay.Visibility != Visibility.Visible) return;
        UpdatePdfImportSelectionSummary();
    }

    private void PdfImportPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string preset } || _pendingPdfImportPageCount <= 0) return;
        PdfImportRangeBox.Text = preset switch
        {
            "Odd" => string.Join(",", Enumerable.Range(1, _pendingPdfImportPageCount).Where(page => page % 2 == 1).Take(PdfImportService.MaximumPageCount)),
            "Even" => string.Join(",", Enumerable.Range(1, _pendingPdfImportPageCount).Where(page => page % 2 == 0).Take(PdfImportService.MaximumPageCount)),
            _ => _pendingPdfImportPageCount <= PdfImportService.MaximumPageCount ? $"1-{_pendingPdfImportPageCount}" : $"1-{PdfImportService.MaximumPageCount}"
        };
    }

    private void UpdatePdfImportSelectionSummary()
    {
        try
        {
            var pages = PdfImportService.ParsePageSelection(PdfImportRangeBox.Text, _pendingPdfImportPageCount);
            PdfImportRangeHintText.Text = $"将导入 {pages.Count} 页 · {SummarizePageSelection(pages)}";
            PdfImportRangeHintText.Foreground = new SolidColorBrush(PageThumbnailService.ParsePaperColor("#315CBB"));
            PdfImportContinueButton.IsEnabled = true;
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

    private void CancelPdfImportOptions_Click(object sender, RoutedEventArgs e) => ClosePdfImportOptions();

    private void ClosePdfImportOptions()
    {
        PdfImportOptionsOverlay.Visibility = Visibility.Collapsed;
        _pendingPdfImportPath = string.Empty;
        _pendingPdfImportPageCount = 0;
        InkSurface.Focus();
    }

    private async void ContinuePdfImport_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null || _isReadOnly || string.IsNullOrWhiteSpace(_pendingPdfImportPath)) return;
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
        PdfImportOptionsOverlay.Visibility = Visibility.Collapsed;
        ImportPdfButton.IsEnabled = false;
        StatusText.Text = $"正在导入 PDF… 0/{pageNumbers.Count} 页";
        try
        {
            var importedPages = await Task.Run(() => PdfImportService.Import(filePath, pageNumbers));
            InsertImportedPdfPages(importedPages, Path.GetFileName(filePath), placement);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            StatusText.Text = "PDF 导入失败";
            MessageBox.Show(this, exception.Message, "无法导入 PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _pendingPdfImportPath = string.Empty;
            _pendingPdfImportPageCount = 0;
            ImportPdfButton.IsEnabled = !_isReadOnly;
        }
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
        StatusText.Text = $"已导入 {newPages.Length} 页 PDF · 插入到第 {insertIndex + 1} 页起";
    }
}
