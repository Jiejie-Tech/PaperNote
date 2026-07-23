using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed record OfflineRecognitionBatchOptions(
    bool ReplaceExisting = false,
    double ReviewConfidenceThreshold = 0.78,
    int MaximumPages = 500);

public sealed record OfflineRecognitionBatchProgress(
    int CompletedPages,
    int TotalPages,
    int RecognizedPages,
    int SkippedPages,
    int FailedPages,
    Guid? CurrentPageId,
    string Message)
{
    public double Fraction => TotalPages == 0 ? 1 : Math.Clamp((double)CompletedPages / TotalPages, 0, 1);
}

public sealed record OfflineRecognitionPageFailure(Guid PageId, int PageNumber, string Message);

public sealed record OfflineRecognitionBatchSummary(
    int CandidatePages,
    int RecognizedPages,
    int SkippedPages,
    int FailedPages,
    int ReviewPages,
    IReadOnlyList<OfflineRecognitionPageFailure> Failures,
    TimeSpan Duration);

/// <summary>Coordinates cancellable, sequential offline OCR for notebook backgrounds and persists review metadata.</summary>
public static class NotebookRecognitionService
{
    public const double DefaultReviewConfidenceThreshold = 0.78;

    public static IReadOnlyList<NotebookPage> GetCandidatePages(NotebookDocument document, bool replaceExisting = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Pages
            .Where(page => !string.IsNullOrWhiteSpace(page.BackgroundImageData))
            .Where(page => replaceExisting || string.IsNullOrWhiteSpace(page.OcrText))
            .ToArray();
    }

    public static void ApplyResult(
        NotebookPage page,
        OfflineRecognitionResult result,
        double reviewConfidenceThreshold = DefaultReviewConfidenceThreshold)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(result);
        reviewConfidenceThreshold = Math.Clamp(reviewConfidenceThreshold, 0, 1);
        page.OcrBlocks = result.Blocks.Select(block => new RecognitionTextBlock
        {
            Text = block.Text,
            Confidence = Math.Clamp(block.Confidence, 0, 1),
            X = Math.Max(0, block.X),
            Y = Math.Max(0, block.Y),
            Width = Math.Max(0, block.Width),
            Height = Math.Max(0, block.Height),
            IsAccepted = true
        }).ToList();
        page.OcrText = OfflineRecognitionService.NormalizeRecognizedText(result.Text);
        page.OcrAverageConfidence = page.OcrBlocks.Count == 0 ? 0 : page.OcrBlocks.Average(block => block.Confidence);
        page.OcrUpdatedAt = DateTimeOffset.Now;
        page.OcrNeedsReview = page.OcrBlocks.Any(block => block.Confidence < reviewConfidenceThreshold);
        page.ModifiedAt = DateTimeOffset.Now;
    }

    public static void ApplyReviewedText(NotebookPage page, string? reviewedText)
    {
        ArgumentNullException.ThrowIfNull(page);
        var normalized = OfflineRecognitionService.NormalizeRecognizedText(reviewedText ?? string.Empty);
        page.OcrText = normalized;
        page.OcrBlocks = string.IsNullOrWhiteSpace(normalized)
            ? []
            : [new RecognitionTextBlock { Text = normalized, Confidence = 1, IsAccepted = true, IsEdited = true }];
        page.OcrAverageConfidence = string.IsNullOrWhiteSpace(normalized) ? 0 : 1;
        page.OcrUpdatedAt = DateTimeOffset.Now;
        page.OcrNeedsReview = false;
        page.ModifiedAt = DateTimeOffset.Now;
    }

    public static string BuildConfidenceSummary(NotebookPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.OcrBlocks.Count == 0) return string.IsNullOrWhiteSpace(page.OcrText) ? "尚未识别" : "旧版识别结果（无逐行置信度）";
        var low = page.OcrBlocks.Count(block => block.Confidence < DefaultReviewConfidenceThreshold);
        return $"平均置信度 {page.OcrAverageConfidence:P0} · {page.OcrBlocks.Count} 行 · 低置信度 {low} 行";
    }

    public static async Task<OfflineRecognitionBatchSummary> RecognizeNotebookAsync(
        NotebookDocument document,
        OfflineRecognitionService recognition,
        OfflineRecognitionBatchOptions? options = null,
        IProgress<OfflineRecognitionBatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(recognition);
        options ??= new OfflineRecognitionBatchOptions();
        var started = DateTimeOffset.UtcNow;
        var allBackgroundPages = document.Pages.Where(page => !string.IsNullOrWhiteSpace(page.BackgroundImageData)).ToArray();
        if (allBackgroundPages.Length > options.MaximumPages)
            throw new InvalidOperationException($"单次最多识别 {options.MaximumPages} 页，请先拆分笔记本或减少页面范围。");

        var failures = new List<OfflineRecognitionPageFailure>();
        var recognized = 0;
        var skipped = 0;
        var review = 0;
        for (var index = 0; index < allBackgroundPages.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = allBackgroundPages[index];
            var pageNumber = document.Pages.IndexOf(page) + 1;
            if (!options.ReplaceExisting && !string.IsNullOrWhiteSpace(page.OcrText))
            {
                skipped++;
                progress?.Report(new OfflineRecognitionBatchProgress(index + 1, allBackgroundPages.Length, recognized, skipped, failures.Count, page.Id, $"第 {pageNumber} 页已有结果，已跳过"));
                continue;
            }

            progress?.Report(new OfflineRecognitionBatchProgress(index, allBackgroundPages.Length, recognized, skipped, failures.Count, page.Id, $"正在识别第 {pageNumber} 页"));
            try
            {
                var bytes = Convert.FromBase64String(page.BackgroundImageData);
                var result = await recognition.RecognizeImageAsync(bytes, cancellationToken).ConfigureAwait(false);
                ApplyResult(page, result, options.ReviewConfidenceThreshold);
                recognized++;
                if (page.OcrNeedsReview) review++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is FormatException or InvalidDataException or FileNotFoundException or TypeInitializationException or DllNotFoundException or InvalidOperationException)
            {
                failures.Add(new OfflineRecognitionPageFailure(page.Id, pageNumber, exception.Message));
            }

            progress?.Report(new OfflineRecognitionBatchProgress(index + 1, allBackgroundPages.Length, recognized, skipped, failures.Count, page.Id, $"已处理 {index + 1}/{allBackgroundPages.Length} 页"));
        }

        document.ModifiedAt = DateTimeOffset.Now;
        return new OfflineRecognitionBatchSummary(allBackgroundPages.Length, recognized, skipped, failures.Count, review, failures, DateTimeOffset.UtcNow - started);
    }
}
