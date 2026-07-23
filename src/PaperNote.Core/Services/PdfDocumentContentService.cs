using PaperNote.Core.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Outline;

namespace PaperNote.Core.Services;

public sealed record PdfExtractedLink(
    double X,
    double Y,
    double Width,
    double Height,
    int TargetPageNumber,
    string Label);

public sealed record PdfExtractedPage(
    int PageNumber,
    string Text,
    IReadOnlyList<PdfExtractedLink> Links,
    IReadOnlyList<PdfTextBlock> TextBlocks);

public sealed record PdfExtractedOutline(
    string Title,
    int Level,
    int TargetPageNumber);

public sealed record PdfDocumentContent(
    IReadOnlyDictionary<int, PdfExtractedPage> Pages,
    IReadOnlyList<PdfExtractedOutline> Outline,
    IReadOnlyList<string> Warnings)
{
    public static PdfDocumentContent Empty { get; } = new(
        new Dictionary<int, PdfExtractedPage>(),
        Array.Empty<PdfExtractedOutline>(),
        Array.Empty<string>());
}

/// <summary>Extracts the existing text layer, outline and internal links locally. No content leaves the device.</summary>
public static class PdfDocumentContentService
{
    public const int MaximumExtractedCharactersPerPage = 200_000;

    public static Task<PdfDocumentContent> ExtractAsync(
        string filePath,
        IReadOnlyCollection<int> pageNumbers,
        IProgress<PdfImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("PDF 路径不能为空。", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("找不到要读取的 PDF。", filePath);
        var requested = pageNumbers.Distinct().OrderBy(number => number).ToArray();
        if (requested.Length == 0) return Task.FromResult(PdfDocumentContent.Empty);
        return Task.Run(() => Extract(filePath, requested, progress, cancellationToken), cancellationToken);
    }

    private static PdfDocumentContent Extract(
        string filePath,
        IReadOnlyList<int> requested,
        IProgress<PdfImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var pages = new Dictionary<int, PdfExtractedPage>();
        var warnings = new List<string>();
        var outline = new List<PdfExtractedOutline>();
        using var document = PdfDocument.Open(filePath);

        for (var index = 0; index < requested.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageNumber = requested[index];
            if (pageNumber < 1 || pageNumber > document.NumberOfPages)
            {
                warnings.Add($"第 {pageNumber} 页超出 PDF 范围，已跳过文本提取。");
                continue;
            }

            progress?.Report(new PdfImportProgress("文本", index, requested.Count, pageNumber, false, $"正在提取第 {pageNumber} 页文本与链接…"));
            try
            {
                var page = document.GetPage(pageNumber);
                var text = NormalizeText(ContentOrderTextExtractor.GetText(page));
                if (text.Length > MaximumExtractedCharactersPerPage)
                {
                    text = text[..MaximumExtractedCharactersPerPage];
                    warnings.Add($"第 {pageNumber} 页文本超过限制，已截断索引内容。");
                }

                var pageWidth = Math.Max(1d, (double)page.Width);
                var pageHeight = Math.Max(1d, (double)page.Height);
                var textBlocks = new List<PdfTextBlock>();
                var readingOrder = 0;
                foreach (var word in page.GetWords().Take(20_000))
                {
                    var value = NormalizeSingleLine(word.Text, 500);
                    if (value.Length == 0) continue;
                    var box = word.BoundingBox;
                    var x = Math.Clamp(box.Left / pageWidth, 0, 1);
                    var y = Math.Clamp((pageHeight - box.Top) / pageHeight, 0, 1);
                    textBlocks.Add(new PdfTextBlock
                    {
                        Text = value,
                        X = x,
                        Y = y,
                        Width = Math.Clamp(box.Width / pageWidth, 0, 1 - x),
                        Height = Math.Clamp(box.Height / pageHeight, 0, 1 - y),
                        ReadingOrder = readingOrder++
                    });
                }

                var links = new List<PdfExtractedLink>();
                foreach (var annotation in page.GetAnnotations())
                {
                    if (annotation.Type != AnnotationType.Link || annotation.Action is not GoToAction goTo) continue;
                    var target = goTo.Destination.PageNumber;
                    if (target <= 0 || target > document.NumberOfPages) continue;
                    var rectangle = annotation.Rectangle;
                    var x = Math.Clamp(rectangle.Left / pageWidth, 0, 1);
                    var y = Math.Clamp((pageHeight - rectangle.Top) / pageHeight, 0, 1);
                    var width = Math.Clamp(rectangle.Width / pageWidth, 0, 1 - x);
                    var height = Math.Clamp(rectangle.Height / pageHeight, 0, 1 - y);
                    links.Add(new PdfExtractedLink(x, y, width, height, target, annotation.Content ?? $"跳转到 PDF 第 {target} 页"));
                }
                pages[pageNumber] = new PdfExtractedPage(pageNumber, text, links, textBlocks);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add($"第 {pageNumber} 页文本层无法读取：{exception.Message}");
                pages[pageNumber] = new PdfExtractedPage(pageNumber, string.Empty, Array.Empty<PdfExtractedLink>(), Array.Empty<PdfTextBlock>());
            }
        }

        try
        {
            if (document.TryGetBookmarks(out var bookmarks))
            {
                foreach (var node in bookmarks.GetNodes())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (node is not DocumentBookmarkNode local || local.PageNumber <= 0) continue;
                    var title = NormalizeSingleLine(node.Title, 200);
                    if (title.Length == 0) continue;
                    outline.Add(new PdfExtractedOutline(title, Math.Clamp(node.Level + 1, 1, 6), local.PageNumber));
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add($"PDF 目录无法读取：{exception.Message}");
        }

        progress?.Report(new PdfImportProgress("文本", requested.Count, requested.Count, requested[^1], false, "PDF 文本层、目录和内部链接已提取"));
        return new PdfDocumentContent(pages, outline, warnings);
    }

    public static void AttachToImportedPages(
        NotebookDocument notebook,
        IReadOnlyList<NotebookPage> importedPages,
        PdfDocumentContent content,
        string sourceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(importedPages);
        ArgumentNullException.ThrowIfNull(content);
        sourceFingerprint = (sourceFingerprint ?? string.Empty).Trim();
        var importedBySourcePage = importedPages
            .Where(page => page.BackgroundPageNumber > 0)
            .GroupBy(page => page.BackgroundPageNumber)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var page in importedPages)
        {
            page.BackgroundSourceFingerprint = sourceFingerprint;
            if (!content.Pages.TryGetValue(page.BackgroundPageNumber, out var extracted)) continue;
            page.PdfText = extracted.Text;
            page.PdfTextBlocks = extracted.TextBlocks.Select(block => block.Clone()).ToList();
            page.PdfLinks = extracted.Links.Select(link => new PdfPageLink
            {
                X = link.X,
                Y = link.Y,
                Width = link.Width,
                Height = link.Height,
                TargetSourcePageNumber = link.TargetPageNumber,
                TargetPageId = ResolveTargetPageId(notebook, importedBySourcePage, sourceFingerprint, link.TargetPageNumber),
                Label = NormalizeSingleLine(link.Label, 160)
            }).ToList();
        }

        foreach (var entry in content.Outline)
        {
            var targetId = ResolveTargetPageId(notebook, importedBySourcePage, sourceFingerprint, entry.TargetPageNumber);
            if (!targetId.HasValue) continue;
            if (notebook.OutlineEntries.Any(existing => existing.TargetPageId == targetId &&
                                                       existing.Level == entry.Level &&
                                                       string.Equals(existing.Title, entry.Title, StringComparison.CurrentCultureIgnoreCase))) continue;
            notebook.OutlineEntries.Add(new DocumentOutlineEntry
            {
                Title = entry.Title,
                Level = entry.Level,
                TargetPageId = targetId,
                SourceFingerprint = sourceFingerprint,
                SourcePageNumber = entry.TargetPageNumber,
                IsImported = true
            });
        }
    }

    public static void ResolveInternalLinks(NotebookDocument notebook)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        foreach (var page in notebook.Pages)
        {
            foreach (var link in page.PdfLinks)
            {
                if (link.TargetPageId.HasValue && notebook.Pages.Any(candidate => candidate.Id == link.TargetPageId.Value)) continue;
                link.TargetPageId = notebook.Pages.FirstOrDefault(candidate =>
                    candidate.BackgroundSourceType == "PDF" &&
                    candidate.BackgroundPageNumber == link.TargetSourcePageNumber &&
                    string.Equals(candidate.BackgroundSourceFingerprint, page.BackgroundSourceFingerprint, StringComparison.OrdinalIgnoreCase))?.Id;
            }
        }
    }

    private static Guid? ResolveTargetPageId(
        NotebookDocument notebook,
        IReadOnlyDictionary<int, NotebookPage> importedBySourcePage,
        string sourceFingerprint,
        int targetSourcePageNumber)
    {
        if (importedBySourcePage.TryGetValue(targetSourcePageNumber, out var imported)) return imported.Id;
        return notebook.Pages.FirstOrDefault(page =>
            page.BackgroundSourceType == "PDF" &&
            page.BackgroundPageNumber == targetSourcePageNumber &&
            string.Equals(page.BackgroundSourceFingerprint, sourceFingerprint, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries);
        return string.Join('\n', lines.Where(line => line.Length > 0));
    }

    private static string NormalizeSingleLine(string? text, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized[..Math.Min(normalized.Length, maximumLength)];
    }
}
