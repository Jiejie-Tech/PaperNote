using System.Windows;
using System.Windows.Controls;
using PaperNote.Core.Models;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<NotebookPage> _pendingPdfExportPages = [];
    private string _pendingPdfExportScopeName = string.Empty;

    private void ShowPdfExportOptions(IReadOnlyList<NotebookPage> pages, string scopeName)
    {
        if (pages.Count == 0) return;
        _pendingPdfExportPages = pages.Select(CloneNotebookPage).ToArray();
        _pendingPdfExportScopeName = scopeName;
        SelectComboBoxItem(PdfExportQualityCombo, "Standard");
        SelectComboBoxItem(PdfExportPageSizeCombo, "A4");
        PdfExportOptionsOverlay.Visibility = Visibility.Visible;
        UpdatePdfExportOptionsSummary();
    }

    private void PdfExportOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || PdfExportOptionsOverlay.Visibility != Visibility.Visible) return;
        UpdatePdfExportOptionsSummary();
    }

    private PdfExportOptions GetSelectedPdfExportOptions()
    {
        var quality = PdfExportQualityCombo.SelectedItem is ComboBoxItem { Tag: string qualityTag } ? qualityTag : "Standard";
        var pageSize = PdfExportPageSizeCombo.SelectedItem is ComboBoxItem { Tag: string pageSizeTag } ? pageSizeTag : "A4";
        return new PdfExportOptions { Quality = quality, PageSize = pageSize }.Normalize();
    }

    private void UpdatePdfExportOptionsSummary()
    {
        if (PdfExportOptionsSummaryText is null) return;
        var options = GetSelectedPdfExportOptions();
        var spec = PdfExportService.GetPageSpec(options);
        var qualityName = options.Quality switch { "Draft" => "较小文件", "High" => "高清", _ => "标准" };
        var pageSizeName = options.PageSize switch { "A5" => "A5", "Letter" => "Letter", _ => "A4" };
        PdfExportOptionsScopeText.Text = $"导出范围：{_pendingPdfExportScopeName} · {_pendingPdfExportPages.Count} 页";
        PdfExportOptionsSummaryText.Text = $"{pageSizeName} · {qualityName} · 每页渲染 {spec.PixelWidth} × {spec.PixelHeight} 像素";
    }

    private void CancelPdfExportOptions_Click(object sender, RoutedEventArgs e) => ClosePdfExportOptions();

    private void ClosePdfExportOptions()
    {
        PdfExportOptionsOverlay.Visibility = Visibility.Collapsed;
        _pendingPdfExportPages = [];
        _pendingPdfExportScopeName = string.Empty;
        InkSurface.Focus();
    }

    private async void ContinuePdfExport_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingPdfExportPages.Count == 0) return;
        var pages = _pendingPdfExportPages;
        var scopeName = _pendingPdfExportScopeName;
        var options = GetSelectedPdfExportOptions();
        PdfExportOptionsOverlay.Visibility = Visibility.Collapsed;
        _pendingPdfExportPages = [];
        _pendingPdfExportScopeName = string.Empty;
        await ExportPagesToPdfAsync(pages, scopeName, options);
    }
}
