using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Mobile.Controls;

namespace PaperNote.Mobile.Pages;

public partial class EditorPage
{
    private CancellationTokenSource? _recognitionCancellation;

    private async Task<string> EnsureOfflineOcrModelsAsync()
    {
        var directory = Path.Combine(FileSystem.AppDataDirectory, "ocr-v6-tiny");
        Directory.CreateDirectory(directory);
        foreach (var fileName in OfflineRecognitionService.RequiredModelFiles)
        {
            var target = Path.Combine(directory, fileName);
            if (File.Exists(target) && new FileInfo(target).Length > 1024) continue;
            var temporary = target + ".tmp";
            await using (var source = await FileSystem.OpenAppPackageFileAsync($"ocr/{fileName}"))
            await using (var destination = File.Create(temporary))
                await source.CopyToAsync(destination);
            File.Move(temporary, target, true);
        }
        return directory;
    }

    private async Task RecognizeCurrentBackgroundAsync()
    {
        if (_page is null || string.IsNullOrWhiteSpace(_page.BackgroundImageData)) return;
        try
        {
            _pageStatus.Text = "正在本机识别页面文字…";
            var directory = await EnsureOfflineOcrModelsAsync();
            using var recognition = new OfflineRecognitionService(directory);
            var result = await recognition.RecognizeImageAsync(Convert.FromBase64String(_page.BackgroundImageData));
            if (!result.HasText)
            {
                await DisplayAlertAsync("离线 OCR", "没有识别到清晰文字。请尝试更清楚、方向正确的页面。", "知道了");
                return;
            }
            NotebookRecognitionService.ApplyResult(_page, result);
            await Clipboard.Default.SetTextAsync(result.Text);
            ScheduleSave();
            _pageStatus.Text = $"已识别 {result.Blocks.Count} 行并复制文字";
            var reviewHint = _page.OcrNeedsReview ? "其中有低置信度内容，可从“更多”打开校对。" : "识别置信度良好。";
            await DisplayAlertAsync("离线 OCR 完成", $"识别结果已保存到当前页搜索索引，并复制到剪贴板。{reviewHint}\n\n{RecognitionPreview(result.Text)}", "完成");
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException or FileNotFoundException or TypeInitializationException or DllNotFoundException)
        {
            _pageStatus.Text = "离线识别失败";
            await DisplayAlertAsync("离线 OCR", $"识别暂时无法完成。\n\n{exception.Message}", "知道了");
        }
    }

    private async Task RecognizeNotebookBackgroundsAsync()
    {
        var notebook = _repository.Current?.Document;
        if (notebook is null || _recognitionCancellation is not null) return;
        var count = notebook.Pages.Count(page => !string.IsNullOrWhiteSpace(page.BackgroundImageData));
        if (count == 0)
        {
            await DisplayAlertAsync("整本离线 OCR", "当前笔记本没有可识别的图片或 PDF 背景页。", "知道了");
            return;
        }
        var existing = notebook.Pages.Count(page => !string.IsNullOrWhiteSpace(page.BackgroundImageData) && !string.IsNullOrWhiteSpace(page.OcrText));
        var mode = await DisplayActionSheetAsync($"整本离线 OCR · {count} 页", "取消", null, $"跳过已有结果（{existing} 页）", "重新识别全部页面");
        if (mode is null or "取消") return;

        _recognitionCancellation = new CancellationTokenSource();
        var token = _recognitionCancellation.Token;
        try
        {
            var directory = await EnsureOfflineOcrModelsAsync();
            using var recognition = new OfflineRecognitionService(directory);
            var progress = new Progress<OfflineRecognitionBatchProgress>(item =>
            {
                _pageStatus.Text = $"{item.Message} · 已识别 {item.RecognizedPages} · 失败 {item.FailedPages}";
            });
            var summary = await NotebookRecognitionService.RecognizeNotebookAsync(
                notebook,
                recognition,
                new OfflineRecognitionBatchOptions(ReplaceExisting: mode == "重新识别全部页面"),
                progress,
                token);
            await _repository.SaveCurrentAsync();
            RefreshPageCards();
            _pageStatus.Text = $"整本 OCR 完成 · 识别 {summary.RecognizedPages} 页 · 待校对 {summary.ReviewPages} 页";
            await DisplayAlertAsync("整本离线 OCR 完成",
                $"识别：{summary.RecognizedPages} 页\n跳过：{summary.SkippedPages} 页\n待校对：{summary.ReviewPages} 页\n失败：{summary.FailedPages} 页",
                "完成");
        }
        catch (OperationCanceledException)
        {
            await _repository.SaveCurrentAsync();
            RefreshPageCards();
            _pageStatus.Text = "批量 OCR 已安全取消，已完成页面已经保存";
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or TypeInitializationException or DllNotFoundException)
        {
            _pageStatus.Text = "整本离线 OCR 失败";
            await DisplayAlertAsync("整本离线 OCR", $"无法完成批量识别。\n\n{exception.Message}", "知道了");
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
        _pageStatus.Text = "正在安全取消批量 OCR…";
    }

    private async Task ReviewCurrentOcrAsync()
    {
        if (_page is null || string.IsNullOrWhiteSpace(_page.OcrText)) return;
        var low = _page.OcrBlocks.Where(block => block.Confidence < NotebookRecognitionService.DefaultReviewConfidenceThreshold).Take(8).ToArray();
        var details = low.Length == 0
            ? NotebookRecognitionService.BuildConfidenceSummary(_page)
            : NotebookRecognitionService.BuildConfidenceSummary(_page) + "\n\n低置信度内容：\n" + string.Join("\n", low.Select(block => $"{block.Confidence:P0}  {block.Text}"));
        await DisplayAlertAsync("OCR 置信度", details, "开始校对");
        var reviewed = await DisplayPromptAsync("校对 OCR 结果", "修改后保存；清空会移除本页 OCR 搜索内容。", "保存", "取消", _page.OcrText, 12000, Keyboard.Text);
        if (reviewed is null) return;
        NotebookRecognitionService.ApplyReviewedText(_page, reviewed);
        ScheduleSave();
        _pageStatus.Text = "当前页 OCR 校对结果已保存并更新搜索索引";
    }

    private async Task RecognizeSelectedInkAsync()
    {
        if (_page is null) return;
        var selectedIds = _canvas.SelectedStrokeIds.ToHashSet();
        var strokes = _page.Ink.Strokes.Where(stroke => selectedIds.Contains(stroke.Id)).ToArray();
        if (strokes.Length == 0) return;
        try
        {
            _pageStatus.Text = "正在本机识别选中手写…";
            var directory = await EnsureOfflineOcrModelsAsync();
            using var recognition = new OfflineRecognitionService(directory);
            var result = await recognition.RecognizeInkAsync(strokes);
            if (!result.HasText)
            {
                await DisplayAlertAsync("手写转文字", "没有识别到清晰文字。请尽量完整框选同一行手写内容。", "知道了");
                return;
            }
            _page.RecognizedText = string.IsNullOrWhiteSpace(_page.RecognizedText) ? result.Text : _page.RecognizedText.Trim() + Environment.NewLine + result.Text;
            var insert = await DisplayAlertAsync("手写转文字", $"识别结果：\n\n{RecognitionPreview(result.Text)}\n\n是否插入为可编辑文字？", "插入文字", "只加入搜索");
            if (insert)
            {
                var minX = strokes.Min(stroke => stroke.Points.Min(point => point.X));
                var minY = strokes.Min(stroke => stroke.Points.Min(point => point.Y));
                var item = new PageObject
                {
                    Kind = "Text", Text = result.Text, X = Math.Clamp(minX, 0, 850), Y = Math.Clamp(minY, 0, 1200),
                    Width = 360, Height = Math.Clamp(80 + result.Text.Length * 1.4, 100, 420), FontSize = 22,
                    LayerId = PageLayerService.EnsureDefault(_page).Id
                };
                _page.Objects.Add(item);
                _canvas.Page = null;
                _canvas.Page = _page;
                SelectTool(InkCanvasTool.Select);
                _canvas.SelectObject(item.Id);
            }
            _page.ModifiedAt = DateTimeOffset.Now;
            ScheduleSave();
            _pageStatus.Text = insert ? "已插入可编辑识别文字" : "识别文字已加入页面搜索";
        }
        catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException or InvalidOperationException or TypeInitializationException or DllNotFoundException)
        {
            _pageStatus.Text = "手写识别失败";
            await DisplayAlertAsync("手写转文字", $"识别暂时无法完成。\n\n{exception.Message}", "知道了");
        }
    }

    private static string RecognitionPreview(string text) => text.Length <= 700 ? text : text[..700] + "…";
}
