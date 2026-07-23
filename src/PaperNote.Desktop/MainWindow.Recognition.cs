using System.IO;
using System.Windows;
using PaperNote.Desktop.Services;
using PaperNote.Core.Models;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private string OfflineOcrModelDirectory => Path.Combine(AppContext.BaseDirectory, "ocr");

    private async Task RecognizeCurrentBackgroundAsync()
    {
        if (_currentPage is null || string.IsNullOrWhiteSpace(_currentPage.BackgroundImageData)) return;
        try
        {
            StatusText.Text = "正在本机识别页面文字…";
            var bytes = Convert.FromBase64String(_currentPage.BackgroundImageData);
            using var recognition = new OfflineRecognitionService(OfflineOcrModelDirectory);
            var result = await recognition.RecognizeImageAsync(bytes);
            if (!result.HasText)
            {
                MessageBox.Show(this, "没有识别到清晰文字。可以尝试导入更清楚、方向正确的页面。", "离线 OCR", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _currentPage.OcrText = result.Text;
            _currentPage.ModifiedAt = DateTimeOffset.Now;
            MarkDirty();
            Clipboard.SetText(result.Text);
            StatusText.Text = $"已离线识别 {result.Blocks.Count} 行并复制到剪贴板";
            MessageBox.Show(this, $"识别结果已保存到当前页搜索索引，并复制到剪贴板。\n\n{PreviewRecognition(result.Text)}", "离线 OCR 完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException or FileNotFoundException or TypeInitializationException or DllNotFoundException)
        {
            MessageBox.Show(this, $"离线识别暂时无法完成。\n\n{exception.Message}", "离线 OCR", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "离线识别失败";
        }
    }

    private async Task RecognizeSelectedInkAsync()
    {
        if (_currentPage is null) return;
        CaptureCurrentPage();
        var selectedIds = InkSurface.GetSelectedStrokes().Select(WpfInkAdapter.GetStrokeId).ToHashSet();
        var strokes = _currentPage.Ink.Strokes.Where(stroke => selectedIds.Contains(stroke.Id)).ToArray();
        if (strokes.Length == 0) return;
        try
        {
            StatusText.Text = "正在本机识别选中手写…";
            using var recognition = new OfflineRecognitionService(OfflineOcrModelDirectory);
            var result = await recognition.RecognizeInkAsync(strokes);
            if (!result.HasText)
            {
                MessageBox.Show(this, "没有识别到清晰文字。请尽量完整框选同一行手写内容。", "手写转文字", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _currentPage.RecognizedText = AppendRecognition(_currentPage.RecognizedText, result.Text);
            var minX = strokes.Min(stroke => stroke.Points.Min(point => point.X));
            var minY = strokes.Min(stroke => stroke.Points.Min(point => point.Y));
            var addText = MessageBox.Show(this, $"识别结果：\n\n{PreviewRecognition(result.Text)}\n\n是否在选区旁插入可编辑文字？", "手写转文字", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (addText)
            {
                AddPageObject(new PageObject
                {
                    Kind = "Text", Text = result.Text, X = Math.Clamp(minX, 0, 850), Y = Math.Clamp(minY, 0, 1200),
                    Width = 360, Height = Math.Clamp(80 + result.Text.Length * 1.4, 100, 420), FontSize = 22,
                    LayerId = PageLayerService.EnsureDefault(_currentPage).Id
                }, "已将手写识别结果插入为可编辑文字");
            }
            else
            {
                _currentPage.ModifiedAt = DateTimeOffset.Now;
                MarkDirty();
                StatusText.Text = "手写识别结果已加入当前页搜索索引";
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException or InvalidOperationException or TypeInitializationException or DllNotFoundException)
        {
            MessageBox.Show(this, $"手写识别暂时无法完成。\n\n{exception.Message}", "手写转文字", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "手写识别失败";
        }
    }

    private static string AppendRecognition(string existing, string value)
        => string.IsNullOrWhiteSpace(existing) ? value : existing.Trim() + Environment.NewLine + value.Trim();

    private static string PreviewRecognition(string value)
        => value.Length <= 700 ? value : value[..700] + "…";
}
