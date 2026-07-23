using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PaperNote.Desktop.Services;
using PaperNote.Core.Models;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private CancellationTokenSource? _recognitionCancellation;
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
            NotebookRecognitionService.ApplyResult(_currentPage, result);
            MarkDirty();
            Clipboard.SetText(result.Text);
            var reviewHint = _currentPage.OcrNeedsReview ? "其中有低置信度内容，建议打开校对。" : "识别置信度良好。";
            StatusText.Text = $"已离线识别 {result.Blocks.Count} 行并复制到剪贴板";
            MessageBox.Show(this, $"识别结果已保存到当前页搜索索引，并复制到剪贴板。{reviewHint}\n\n{PreviewRecognition(result.Text)}", "离线 OCR 完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException or FileNotFoundException or TypeInitializationException or DllNotFoundException)
        {
            MessageBox.Show(this, $"离线识别暂时无法完成。\n\n{exception.Message}", "离线 OCR", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "离线识别失败";
        }
    }

    private async Task RecognizeNotebookBackgroundsAsync()
    {
        if (_currentNotebook is null || _isReadOnly || _recognitionCancellation is not null) return;
        CaptureCurrentPage();
        var backgroundCount = _currentNotebook.Pages.Count(page => !string.IsNullOrWhiteSpace(page.BackgroundImageData));
        if (backgroundCount == 0)
        {
            MessageBox.Show(this, "当前笔记本没有可识别的图片或 PDF 背景页。", "整本离线 OCR", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var existingCount = _currentNotebook.Pages.Count(page => !string.IsNullOrWhiteSpace(page.BackgroundImageData) && !string.IsNullOrWhiteSpace(page.OcrText));
        var choice = MessageBox.Show(this,
            $"将完全在本机依次识别 {backgroundCount} 个背景页。\n\n选择“是”：重新识别全部页面。\n选择“否”：跳过已有识别结果的 {existingCount} 页。\n选择“取消”：不开始。",
            "整本离线 OCR", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel) return;

        _recognitionCancellation = new CancellationTokenSource();
        var token = _recognitionCancellation.Token;
        OfflineRecognitionBatchSummary? summary = null;
        try
        {
            using var recognition = new OfflineRecognitionService(OfflineOcrModelDirectory);
            var progress = new Progress<OfflineRecognitionBatchProgress>(item =>
            {
                StatusText.Text = $"{item.Message} · 已识别 {item.RecognizedPages} · 失败 {item.FailedPages} · 再点“文字”可取消";
            });
            summary = await NotebookRecognitionService.RecognizeNotebookAsync(
                _currentNotebook,
                recognition,
                new OfflineRecognitionBatchOptions(ReplaceExisting: choice == MessageBoxResult.Yes),
                progress,
                token);
            MarkDirty();
            await SaveNotebookAsync();
            StatusText.Text = $"整本 OCR 完成 · 识别 {summary.RecognizedPages} 页 · 待校对 {summary.ReviewPages} 页";
            var failed = summary.Failures.Count == 0 ? string.Empty : $"\n失败 {summary.FailedPages} 页，可稍后重新运行。";
            MessageBox.Show(this,
                $"整本离线 OCR 已完成。\n\n识别：{summary.RecognizedPages} 页\n跳过：{summary.SkippedPages} 页\n待校对：{summary.ReviewPages} 页{failed}",
                "整本离线 OCR", MessageBoxButton.OK, summary.FailedPages == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            MarkDirty();
            await SaveNotebookAsync();
            StatusText.Text = "批量 OCR 已安全取消，已完成的页面已经保存";
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or TypeInitializationException or DllNotFoundException)
        {
            MessageBox.Show(this, $"无法完成整本离线识别。\n\n{exception.Message}", "整本离线 OCR", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "整本离线 OCR 失败";
        }
        finally
        {
            _recognitionCancellation.Dispose();
            _recognitionCancellation = null;
        }
    }

    private void CancelNotebookRecognition()
    {
        if (_recognitionCancellation is null) return;
        _recognitionCancellation.Cancel();
        StatusText.Text = "正在安全取消批量 OCR…";
    }

    private void ReviewCurrentOcr()
    {
        if (_currentPage is null || string.IsNullOrWhiteSpace(_currentPage.OcrText)) return;
        var summary = new TextBlock
        {
            Text = NotebookRecognitionService.BuildConfidenceSummary(_currentPage) + (_currentPage.OcrNeedsReview ? " · 建议校对低置信度行" : ""),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_currentPage.OcrNeedsReview ? "#B45309" : "#047857")),
            TextWrapping = TextWrapping.Wrap
        };
        var lowConfidence = _currentPage.OcrBlocks
            .Where(block => block.Confidence < NotebookRecognitionService.DefaultReviewConfidenceThreshold)
            .Take(20)
            .Select(block => $"{block.Confidence:P0}  {block.Text}")
            .ToArray();
        var lowText = new TextBlock
        {
            Text = lowConfidence.Length == 0 ? "没有低置信度行。" : "低置信度内容：\n" + string.Join("\n", lowConfidence),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280")),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 150
        };
        var editor = new TextBox
        {
            Text = _currentPage.OcrText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 280,
            FontSize = 15
        };
        var save = new Button { Content = "保存校对结果", MinWidth = 118, Height = 34, IsDefault = true };
        var cancel = new Button { Content = "取消", MinWidth = 80, Height = 34, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save); buttons.Children.Add(cancel);
        var panel = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(summary, Dock.Top); panel.Children.Add(summary);
        DockPanel.SetDock(lowText, Dock.Top); lowText.Margin = new Thickness(0, 10, 0, 10); panel.Children.Add(lowText);
        DockPanel.SetDock(buttons, Dock.Bottom); buttons.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(buttons);
        panel.Children.Add(editor);
        var dialog = new Window
        {
            Title = "OCR 结果校对",
            Owner = this,
            Width = 720,
            Height = 620,
            MinWidth = 520,
            MinHeight = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        save.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true) return;
        NotebookRecognitionService.ApplyReviewedText(_currentPage, editor.Text);
        MarkDirty();
        StatusText.Text = "当前页 OCR 校对结果已保存并更新搜索索引";
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
