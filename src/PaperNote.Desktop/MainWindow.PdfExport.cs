using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using PaperNote.Core.Models;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null) return;
        CaptureCurrentPage();
        ShowPdfExportOptions(_currentNotebook.Pages, "全部页面");
    }

    private void ExportSelectedPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null) return;
        CaptureCurrentPage();
        var selectedIds = GetSelectedPageIds();
        var pages = _currentNotebook.Pages.Where(page => selectedIds.Contains(page.Id)).ToArray();
        if (pages.Length == 0 && _currentPage is not null) pages = [_currentPage];
        ShowPdfExportOptions(pages, pages.Length == 1 ? "当前页面" : $"选中的 {pages.Length} 页");
    }

    private async Task ExportPagesToPdfAsync(IReadOnlyList<NotebookPage> pages, string scopeName, PdfExportOptions options)
    {
        if (_currentNotebook is null || pages.Count == 0) return;
        var safeTitle = string.Join("_", _currentNotebook.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "PaperNote";
        var dialog = new SaveFileDialog
        {
            Title = $"导出{scopeName}为 PDF",
            Filter = "PDF 文件 (*.pdf)|*.pdf",
            FileName = $"{safeTitle}-{DateTime.Now:yyyyMMdd-HHmm}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            AddExtension = true,
            DefaultExt = ".pdf"
        };
        if (dialog.ShowDialog(this) != true) return;

        ExportPdfButton.IsEnabled = false;
        ExportSelectedPdfButton.IsEnabled = false;
        var snapshots = pages.Select(page => page.Clone(preserveIdentity: true)).ToArray();
        var progress = new Progress<int>(completed => StatusText.Text = $"正在导出 PDF… {completed}/{snapshots.Length} 页");
        StatusText.Text = $"正在导出{scopeName}…";
        try
        {
            await PdfExportService.ExportAsync(dialog.FileName, snapshots, options, progress);
            StatusText.Text = $"已导出 {snapshots.Length} 页 · {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            StatusText.Text = "PDF 导出失败";
            MessageBox.Show(this, $"无法导出 PDF。\n\n{exception.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ExportPdfButton.IsEnabled = true;
            ExportSelectedPdfButton.IsEnabled = true;
        }
    }

    private void PdfPageActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var isPdfPage = _currentPage is { BackgroundSourceType: "PDF" } && !string.IsNullOrWhiteSpace(_currentPage.BackgroundImageData);
        var canEdit = isPdfPage && !_isReadOnly;
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("左转 90°", "", (_, _) => RotateCurrentPdfPage(-90), canEdit));
        menu.Items.Add(CreateMenuItem("右转 90°", "", (_, _) => RotateCurrentPdfPage(90), canEdit));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("轻度裁边（5%）", "", (_, _) => SetCurrentPdfCrop(0.05), canEdit));
        menu.Items.Add(CreateMenuItem("深度裁边（10%）", "", (_, _) => SetCurrentPdfCrop(0.10), canEdit));
        menu.Items.Add(CreateMenuItem("自定义裁剪…", "", (_, _) => OpenCustomPdfCropEditor(), canEdit));
        menu.Items.Add(CreateMenuItem("清除裁剪", "", (_, _) => SetCurrentPdfCrop(0), canEdit));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("恢复原始方向和裁剪", "", (_, _) => ResetCurrentPdfPageTransform(), canEdit));
        if (!isPdfPage) menu.Items.Add(new MenuItem { Header = "当前页不是 PDF 页面", IsEnabled = false });
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private bool RotateCurrentPdfPage(int delta)
    {
        if (!CanEditCurrentPdfPage()) return false;
        _currentPage!.BackgroundRotation = ((_currentPage.BackgroundRotation + delta) % 360 + 360) % 360;
        ApplyPdfPageTransform($"PDF 页面已{(delta < 0 ? "左转" : "右转")} 90°");
        return true;
    }

    private bool SetCurrentPdfCrop(double amount)
    {
        amount = Math.Clamp(amount, 0, 0.2);
        return SetCurrentPdfCrop(amount, amount, amount, amount);
    }

    private bool SetCurrentPdfCrop(double left, double top, double right, double bottom)
    {
        if (!CanEditCurrentPdfPage()) return false;
        left = Math.Clamp(left, 0, 0.45);
        top = Math.Clamp(top, 0, 0.45);
        right = Math.Clamp(right, 0, Math.Min(0.45, 0.9 - left));
        bottom = Math.Clamp(bottom, 0, Math.Min(0.45, 0.9 - top));
        _currentPage!.BackgroundCropLeft = left;
        _currentPage.BackgroundCropTop = top;
        _currentPage.BackgroundCropRight = right;
        _currentPage.BackgroundCropBottom = bottom;
        var cleared = left <= 0 && top <= 0 && right <= 0 && bottom <= 0;
        ApplyPdfPageTransform(cleared ? "已清除 PDF 页面裁剪" : $"PDF 自定义裁剪已应用 · 左 {left:P0} / 上 {top:P0} / 右 {right:P0} / 下 {bottom:P0}");
        return true;
    }

    private bool ResetCurrentPdfPageTransform()
    {
        if (!CanEditCurrentPdfPage()) return false;
        _currentPage!.BackgroundRotation = 0;
        _currentPage.BackgroundCropLeft = _currentPage.BackgroundCropTop = _currentPage.BackgroundCropRight = _currentPage.BackgroundCropBottom = 0;
        ApplyPdfPageTransform("PDF 页面已恢复原始方向和裁剪");
        return true;
    }

    private bool CanEditCurrentPdfPage()
    {
        return !_isReadOnly && _currentPage is { BackgroundSourceType: "PDF" } && !string.IsNullOrWhiteSpace(_currentPage.BackgroundImageData);
    }

    private void ApplyPdfPageTransform(string status)
    {
        if (_currentPage is null) return;
        PageBackgroundImage.Source = PageBackgroundService.CreateImageSource(_currentPage);
        _currentPage.ModifiedAt = DateTimeOffset.Now;
        UpdateCurrentPageThumbnail();
        MarkDirty();
        StatusText.Text = status;
    }
}
