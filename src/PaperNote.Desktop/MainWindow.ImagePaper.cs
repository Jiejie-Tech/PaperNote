using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using PaperNote.Desktop.Models;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private void ImagePaperActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var canEdit = _currentNotebook is not null && _currentPage is not null && !_isReadOnly;
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("替换当前页背景", "", (_, _) => ImportImagePaper("ReplaceCurrent"), canEdit));
        menu.Items.Add(CreateMenuItem("在当前页后插入", "", (_, _) => ImportImagePaper("AfterCurrent"), canEdit));
        menu.Items.Add(CreateMenuItem("添加到笔记末尾", "", (_, _) => ImportImagePaper("Append"), canEdit));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("清除当前页图片背景", "", (_, _) => ClearCurrentImagePaper(), canEdit && string.Equals(_currentPage?.BackgroundSourceType, "Image", StringComparison.OrdinalIgnoreCase)));
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ImportImagePaper(string mode)
    {
        if (_currentNotebook is null || _currentPage is null || _isReadOnly) return;
        var dialog = new OpenFileDialog
        {
            Title = mode == "ReplaceCurrent" ? "选择当前页的图片纸张" : "选择要导入为纸张的图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",
            Multiselect = mode != "ReplaceCurrent"
        };
        if (dialog.ShowDialog(this) != true) return;
        if (dialog.FileNames.Length > 100)
        {
            MessageBox.Show(this, "一次最多导入 100 张图片。", "图片过多", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var images = dialog.FileNames.Select(ReadImagePaper).ToArray();
            if (mode == "ReplaceCurrent")
            {
                ApplyImagePaper(_currentPage, images[0]);
                ApplyPageAppearance(_currentPage);
                UpdateCurrentPageMetadataControls(_currentPage);
                UpdateCurrentPageThumbnail();
                MarkDirty();
                StatusText.Text = $"当前页已使用图片纸张：{images[0].Name}";
                return;
            }

            CaptureCurrentPage();
            var insertIndex = mode == "Append"
                ? _currentNotebook.Pages.Count
                : Math.Max(0, _currentNotebook.Pages.IndexOf(_currentPage) + 1);
            var pages = images.Select(image =>
            {
                var page = new NotebookPage
                {
                    Title = Path.GetFileNameWithoutExtension(image.Name),
                    PaperTemplate = "Blank",
                    PaperColor = PaperPageDefaults.Color
                };
                ApplyImagePaper(page, image);
                return page;
            }).ToArray();
            _currentNotebook.Pages.InsertRange(insertIndex, pages);
            _currentPage = pages[0];
            _currentNotebook.CurrentPageId = _currentPage.Id;
            RefreshPageItems(_currentPage.Id);
            LoadPage(_currentPage);
            MarkDirty();
            StatusText.Text = $"已导入 {pages.Length} 张图片纸张";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException)
        {
            MessageBox.Show(this, $"无法导入图片纸张。\n\n{exception.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static (string Name, string Base64) ReadImagePaper(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length == 0) throw new InvalidOperationException($"图片为空：{Path.GetFileName(filePath)}");
        if (bytes.Length > 20 * 1024 * 1024) throw new InvalidOperationException($"单张图片不能超过 20 MB：{Path.GetFileName(filePath)}");
        _ = DecodeImage(bytes);
        return (Path.GetFileName(filePath), Convert.ToBase64String(bytes));
    }

    private static void ApplyImagePaper(NotebookPage page, (string Name, string Base64) image)
    {
        page.PaperTemplate = "Blank";
        page.BackgroundImageData = image.Base64;
        page.BackgroundSourceType = "Image";
        page.BackgroundSourceName = image.Name;
        page.BackgroundPageNumber = 0;
        page.BackgroundRotation = 0;
        page.BackgroundCropLeft = 0;
        page.BackgroundCropTop = 0;
        page.BackgroundCropRight = 0;
        page.BackgroundCropBottom = 0;
        page.ModifiedAt = DateTimeOffset.Now;
    }

    private bool ClearCurrentImagePaper()
    {
        if (_currentPage is null || _isReadOnly || !string.Equals(_currentPage.BackgroundSourceType, "Image", StringComparison.OrdinalIgnoreCase)) return false;
        _currentPage.BackgroundImageData = string.Empty;
        _currentPage.BackgroundSourceType = string.Empty;
        _currentPage.BackgroundSourceName = string.Empty;
        _currentPage.BackgroundPageNumber = 0;
        _currentPage.BackgroundRotation = 0;
        _currentPage.BackgroundCropLeft = _currentPage.BackgroundCropTop = _currentPage.BackgroundCropRight = _currentPage.BackgroundCropBottom = 0;
        _currentPage.ModifiedAt = DateTimeOffset.Now;
        ApplyPageAppearance(_currentPage);
        UpdateCurrentPageThumbnail();
        MarkDirty();
        StatusText.Text = "已清除当前页图片背景";
        return true;
    }
}
