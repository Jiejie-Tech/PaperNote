using PaperNote.Core.Ink;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public enum PageAnnotationKind
{
    Comment,
    Highlighter,
    Pen,
    Text,
    Image,
    Shape
}

public sealed record PageAnnotationSummary(
    Guid Id,
    Guid PageId,
    int PageNumber,
    PageAnnotationKind Kind,
    string Color,
    string Preview,
    DateTimeOffset ModifiedAt);

public static class PageAnnotationService
{
    public static IReadOnlyList<PageAnnotationSummary> Build(
        NotebookDocument notebook,
        PageAnnotationKind? kind = null,
        string? color = null)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        var results = new List<PageAnnotationSummary>();
        for (var index = 0; index < notebook.Pages.Count; index++)
        {
            var page = notebook.Pages[index];
            foreach (var comment in page.Comments)
                results.Add(new PageAnnotationSummary(comment.Id, page.Id, index + 1, PageAnnotationKind.Comment, comment.Color, comment.Text, comment.ModifiedAt));
            foreach (var stroke in page.Ink.Strokes)
            {
                var strokeKind = stroke.Tool == PaperInkTool.Highlighter ? PageAnnotationKind.Highlighter : PageAnnotationKind.Pen;
                results.Add(new PageAnnotationSummary(stroke.Id, page.Id, index + 1, strokeKind, stroke.Color,
                    strokeKind == PageAnnotationKind.Highlighter ? "荧光标注" : "手写笔迹", page.ModifiedAt));
            }
            foreach (var item in page.Objects.Where(item => !item.IsHidden))
            {
                var itemKind = item.Kind switch
                {
                    "Image" => PageAnnotationKind.Image,
                    "Shape" => PageAnnotationKind.Shape,
                    _ => PageAnnotationKind.Text
                };
                var preview = itemKind == PageAnnotationKind.Text
                    ? item.Text
                    : itemKind == PageAnnotationKind.Shape ? $"{item.ShapeKind} 形状" : "图片";
                results.Add(new PageAnnotationSummary(item.Id, page.Id, index + 1, itemKind, item.StrokeColor, preview, page.ModifiedAt));
            }
        }

        return results
            .Where(item => kind is null || item.Kind == kind)
            .Where(item => string.IsNullOrWhiteSpace(color) || string.Equals(item.Color, color, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.PageNumber)
            .ThenByDescending(item => item.ModifiedAt)
            .ToArray();
    }

    public static PageComment AddComment(NotebookPage page, string text, string color = "#F0B429", double x = 24, double y = 24)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("批注内容不能为空。", nameof(text));
        var comment = new PageComment
        {
            Text = text.Trim()[..Math.Min(text.Trim().Length, 2000)],
            Color = string.IsNullOrWhiteSpace(color) ? "#F0B429" : color.Trim(),
            X = Math.Clamp(double.IsFinite(x) ? x : 24, 0, 840),
            Y = Math.Clamp(double.IsFinite(y) ? y : 24, 0, 1188)
        };
        page.Comments.Add(comment);
        page.ModifiedAt = DateTimeOffset.Now;
        return comment;
    }

    public static bool DeleteComment(NotebookPage page, Guid commentId)
    {
        ArgumentNullException.ThrowIfNull(page);
        var removed = page.Comments.RemoveAll(comment => comment.Id == commentId) > 0;
        if (removed) page.ModifiedAt = DateTimeOffset.Now;
        return removed;
    }
}
