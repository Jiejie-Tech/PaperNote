using System.Text;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public static class NotebookContentService
{
    public static bool TryMatch(NotebookDocument document, string? query, out string matchSummary)
    {
        matchSummary = string.Empty;
        var terms = SplitTerms(query);
        if (terms.Length == 0) return true;

        var fields = EnumerateSearchFields(document).ToArray();
        var searchable = string.Join("\n", fields.Select(field => field.Value));
        if (!terms.All(term => searchable.Contains(term, StringComparison.CurrentCultureIgnoreCase))) return false;

        var best = fields.FirstOrDefault(field => terms.Any(term => field.Value.Contains(term, StringComparison.CurrentCultureIgnoreCase)));
        matchSummary = best.Label;
        return true;
    }

    public static string BuildPagePlainText(NotebookDocument document, NotebookPage page)
    {
        var builder = new StringBuilder();
        var pageIndex = Math.Max(0, document.Pages.IndexOf(page));
        builder.AppendLine($"第 {pageIndex + 1} 页{(string.IsNullOrWhiteSpace(page.Title) ? string.Empty : $" · {page.Title.Trim()}")}");

        if (page.Tags.Count > 0) builder.AppendLine($"\u6807\u7b7e\uff1a{string.Join("\u3001", page.Tags)}");
        if (!string.IsNullOrWhiteSpace(page.OcrText)) builder.AppendLine(page.OcrText.Trim());
        if (!string.IsNullOrWhiteSpace(page.RecognizedText)) builder.AppendLine(page.RecognizedText.Trim());

        var textObjects = page.Objects.Where(item => item.Kind == "Text" && !item.IsHidden && !string.IsNullOrWhiteSpace(item.Text)).ToArray();
        foreach (var item in textObjects) builder.AppendLine(item.Text.Trim());

        foreach (var item in page.Objects.Where(item => item.LinkTargetPageId.HasValue))
        {
            var targetIndex = document.Pages.FindIndex(candidate => candidate.Id == item.LinkTargetPageId);
            if (targetIndex < 0) continue;
            var target = document.Pages[targetIndex];
            var targetTitle = string.IsNullOrWhiteSpace(target.Title) ? string.Empty : $" · {target.Title.Trim()}";
            builder.AppendLine($"[页面链接：第 {targetIndex + 1} 页{targetTitle}]");
        }

        if (!string.IsNullOrWhiteSpace(page.BackgroundSourceName))
        {
            var sourceType = page.BackgroundSourceType == "PDF" ? "PDF 来源" : "图片来源";
            var pageNumber = page.BackgroundSourceType == "PDF" && page.BackgroundPageNumber > 0 ? $" · 原第 {page.BackgroundPageNumber} 页" : string.Empty;
            builder.AppendLine($"[{sourceType}：{page.BackgroundSourceName.Trim()}{pageNumber}]");
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildNotebookPlainText(NotebookDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine(document.Title.Trim());
        if (!string.IsNullOrWhiteSpace(document.FolderName)) builder.AppendLine($"文件夹：{document.FolderName.Trim()}");
        builder.AppendLine($"共 {document.Pages.Count} 页");
        builder.AppendLine();
        for (var index = 0; index < document.Pages.Count; index++)
        {
            if (index > 0)
            {
                builder.AppendLine();
                builder.AppendLine(new string('-', 32));
                builder.AppendLine();
            }
            builder.AppendLine(BuildPagePlainText(document, document.Pages[index]));
        }
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string[] SplitTerms(string? query)
    {
        return (query ?? string.Empty)
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(string Label, string Value)> EnumerateSearchFields(NotebookDocument document)
    {
        yield return ("\u7b14\u8bb0\u672c\u6807\u9898", document.Title ?? string.Empty);
        yield return ("\u6587\u4ef6\u5939", document.FolderName ?? string.Empty);
        foreach (var tag in document.Tags) yield return ("\u7b14\u8bb0\u672c\u6807\u7b7e", tag);
        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            if (!string.IsNullOrWhiteSpace(page.Title)) yield return ($"\u7b2c {index + 1} \u9875\u6807\u9898", page.Title);
            foreach (var tag in page.Tags) yield return ($"\u7b2c {index + 1} \u9875\u6807\u7b7e", tag);
            if (!string.IsNullOrWhiteSpace(page.OcrText)) yield return ($"\u7b2c {index + 1} \u9875 OCR", page.OcrText);
            if (!string.IsNullOrWhiteSpace(page.RecognizedText)) yield return ($"\u7b2c {index + 1} \u9875\u624b\u5199\u8bc6\u522b", page.RecognizedText);
            foreach (var item in page.Objects.Where(item => item.Kind == "Text" && !item.IsHidden && !string.IsNullOrWhiteSpace(item.Text)))
                yield return ($"\u7b2c {index + 1} \u9875\u6587\u5b57", item.Text);
            if (!string.IsNullOrWhiteSpace(page.BackgroundSourceName))
                yield return ($"\u7b2c {index + 1} \u9875\u6765\u6e90", page.BackgroundSourceName);
        }
    }
}
