using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private void TextActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var hasNotebook = _currentNotebook is not null;
        var hasPage = _currentNotebook is not null && _currentPage is not null;
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("识别当前页背景文字（离线 OCR）", "", async (_, _) => await RecognizeCurrentBackgroundAsync(), hasPage && !string.IsNullOrWhiteSpace(_currentPage?.BackgroundImageData)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("复制当前页文字", "", (_, _) => CopyCurrentPageText(), hasPage));
        menu.Items.Add(CreateMenuItem("复制整本笔记文字", "", (_, _) => CopyNotebookText(), hasNotebook));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("导出整本为 TXT", "", (_, _) => ExportNotebookText(), hasNotebook));
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void CopyCurrentPageText()
    {
        if (_currentNotebook is null || _currentPage is null) return;
        CaptureCurrentPage();
        CopyTextToClipboard(NotebookContentService.BuildPagePlainText(_currentNotebook, _currentPage), "已复制当前页可提取文字");
    }

    private void CopyNotebookText()
    {
        if (_currentNotebook is null) return;
        CaptureCurrentPage();
        CopyTextToClipboard(NotebookContentService.BuildNotebookPlainText(_currentNotebook), "已复制整本笔记可提取文字");
    }

    private void CopyTextToClipboard(string text, string successMessage)
    {
        try
        {
            Clipboard.SetText(text);
            StatusText.Text = successMessage;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            MessageBox.Show(this, $"暂时无法访问系统剪贴板。\n\n{exception.Message}", "复制失败", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportNotebookText()
    {
        if (_currentNotebook is null) return;
        CaptureCurrentPage();
        var dialog = new SaveFileDialog
        {
            Title = "导出整本笔记文字",
            Filter = "纯文本文件|*.txt",
            FileName = $"{NotebookStorageService.MakeSafeFileName(_currentNotebook.Title)}.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, NotebookContentService.BuildNotebookPlainText(_currentNotebook), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusText.Text = $"已导出文字：{Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(this, $"无法导出文字文件。\n\n{exception.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
