using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed record OfflineSearchHit(Guid NotebookId, Guid PageId, int PageNumber, string Source, string Snippet, int Score);

/// <summary>仅在本地内存中建立笔记内容索引，不上传或依赖网络服务。</summary>
public sealed class OfflineSearchService
{
    private readonly List<SearchEntry> _entries = [];
    public int Count => _entries.Count;

    public void Rebuild(IEnumerable<NotebookDocument> documents)
    {
        _entries.Clear();
        foreach (var document in documents) Index(document);
    }

    public void Index(NotebookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _entries.RemoveAll(entry => entry.NotebookId == document.Id);
        Add(document.Id, Guid.Empty, 0, "笔记本标题", document.Title, 8);
        Add(document.Id, Guid.Empty, 0, "文件夹", document.FolderName, 4);
        foreach (var tag in document.Tags) Add(document.Id, Guid.Empty, 0, "笔记本标签", tag, 5);
        for (var i = 0; i < document.Pages.Count; i++)
        {
            var page = document.Pages[i];
            Add(document.Id, page.Id, i + 1, "页面标题", page.Title, 7);
            Add(document.Id, page.Id, i + 1, "OCR", page.OcrText, 3);
            Add(document.Id, page.Id, i + 1, "手写识别", page.RecognizedText, 3);
            Add(document.Id, page.Id, i + 1, "PDF 文本", page.PdfText, 6);
            foreach (var comment in page.Comments) Add(document.Id, page.Id, i + 1, "文字评论", comment.Text, 6);
            foreach (var tag in page.Tags) Add(document.Id, page.Id, i + 1, "页面标签", tag, 5);
            foreach (var item in page.Objects.Where(item => !item.IsHidden))
                Add(document.Id, page.Id, i + 1, item.Kind == "Text" ? "文字" : "对象", item.Kind == "Text" ? item.Text : item.Text + " " + item.ShapeKind, 6);
            Add(document.Id, page.Id, i + 1, "资料来源", page.BackgroundSourceName, 2);
        }
    }

    public IReadOnlyList<OfflineSearchHit> Search(string? query, int limit = 100)
    {
        var terms = SplitTerms(query);
        if (terms.Length == 0) return [];
        return _entries.Where(entry => terms.All(term => entry.Text.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            .Select(entry => new OfflineSearchHit(entry.NotebookId, entry.PageId, entry.PageNumber, entry.Source, MakeSnippet(entry.Text, terms), entry.Weight + terms.Length))
            .OrderByDescending(hit => hit.Score).ThenBy(hit => hit.PageNumber).Take(Math.Clamp(limit, 1, 1000)).ToArray();
    }

    public static string BuildIndexText(NotebookDocument document, NotebookPage page)
        => string.Join("\n", new[] { document.Title, document.FolderName, string.Join(' ', document.Tags), page.Title, string.Join(' ', page.Tags), page.OcrText, page.RecognizedText, page.PdfText, string.Join(' ', page.Comments.Select(comment => comment.Text)), NotebookContentService.BuildPagePlainText(document, page) }.Where(text => !string.IsNullOrWhiteSpace(text)));

    private void Add(Guid notebookId, Guid pageId, int pageNumber, string source, string? text, int weight)
    {
        if (!string.IsNullOrWhiteSpace(text)) _entries.Add(new SearchEntry(notebookId, pageId, pageNumber, source, text.Trim(), weight));
    }

    private static string[] SplitTerms(string? query) => (query ?? string.Empty).Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();

    private static string MakeSnippet(string text, string[] terms)
    {
        var index = terms.Select(term => text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase)).Where(index => index >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, index - 24); var length = Math.Min(120, text.Length - start);
        return (start > 0 ? "…" : string.Empty) + text.Substring(start, length).Trim() + (start + length < text.Length ? "…" : string.Empty);
    }

    private sealed record SearchEntry(Guid NotebookId, Guid PageId, int PageNumber, string Source, string Text, int Weight);
}
