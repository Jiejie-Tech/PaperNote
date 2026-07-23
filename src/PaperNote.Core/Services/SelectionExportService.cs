using PaperNote.Core.Ink;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed record SelectionExportSnapshot(NotebookPage Page, double X, double Y, double Width, double Height)
{
    public bool HasContent => Page.Ink.Strokes.Count > 0 || Page.Objects.Count > 0;
}

public static class SelectionExportService
{
    public const double DefaultPageWidth = 840;
    public const double DefaultPageHeight = 1188;

    public static SelectionExportSnapshot? Create(
        NotebookPage page,
        IEnumerable<Guid> strokeIds,
        IEnumerable<Guid> objectIds,
        double padding = 20,
        double pageWidth = DefaultPageWidth,
        double pageHeight = DefaultPageHeight)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(strokeIds);
        ArgumentNullException.ThrowIfNull(objectIds);
        if (!double.IsFinite(pageWidth) || pageWidth <= 0 || !double.IsFinite(pageHeight) || pageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageWidth));

        var strokeSet = strokeIds.Where(id => id != Guid.Empty).ToHashSet();
        var objectSet = PageObjectEditingService.ExpandSelection(page, objectIds).Where(id => id != Guid.Empty).ToHashSet();
        var strokes = page.Ink.Strokes
            .Where(stroke => strokeSet.Contains(stroke.Id) && stroke.Points.Count > 0 && PageLayerService.IsContentVisible(page, stroke.LayerId))
            .Select(stroke =>
            {
                var clone = stroke.Clone();
                clone.Opacity = PageLayerService.GetEffectiveOpacity(page, clone.LayerId, clone.Opacity);
                clone.LayerId = null;
                return clone;
            })
            .ToList();
        var objects = page.Objects
            .Where(item => objectSet.Contains(item.Id) && !item.IsHidden && PageLayerService.IsContentVisible(page, item.LayerId))
            .Select(item =>
            {
                var clone = item.Clone();
                clone.Opacity = PageLayerService.GetEffectiveOpacity(page, clone.LayerId, clone.Opacity);
                clone.LayerId = null;
                clone.IsLocked = false;
                return clone;
            })
            .ToList();
        if (strokes.Count == 0 && objects.Count == 0) return null;

        var bounds = ComputeBounds(strokes, objects);
        padding = double.IsFinite(padding) ? Math.Clamp(padding, 0, 160) : 20;
        var left = Math.Clamp(bounds.Left - padding, 0, pageWidth);
        var top = Math.Clamp(bounds.Top - padding, 0, pageHeight);
        var right = Math.Clamp(bounds.Right + padding, left + 1, pageWidth);
        var bottom = Math.Clamp(bounds.Bottom + padding, top + 1, pageHeight);

        var snapshot = new NotebookPage
        {
            Title = string.IsNullOrWhiteSpace(page.Title) ? "选区" : $"{page.Title} · 选区",
            PaperTemplate = "Blank",
            PaperColor = "#FFFFFF",
            Ink = new PaperInkDocument { Version = page.Ink.Version, Strokes = strokes },
            Objects = objects
        };
        return new SelectionExportSnapshot(snapshot, left, top, right - left, bottom - top);
    }

    private static SelectionBounds ComputeBounds(IReadOnlyList<PaperInkStroke> strokes, IReadOnlyList<PageObject> objects)
    {
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;

        foreach (var stroke in strokes)
        {
            var halfWidth = Math.Max(.5, stroke.Width / 2d);
            foreach (var point in stroke.Points)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) continue;
                left = Math.Min(left, point.X - halfWidth);
                top = Math.Min(top, point.Y - halfWidth);
                right = Math.Max(right, point.X + halfWidth);
                bottom = Math.Max(bottom, point.Y + halfWidth);
            }
        }

        foreach (var item in objects)
        {
            var centerX = item.X + item.Width / 2d;
            var centerY = item.Y + item.Height / 2d;
            var radians = item.Rotation * Math.PI / 180d;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            foreach (var (x, y) in new[]
                     {
                         (item.X, item.Y), (item.X + item.Width, item.Y),
                         (item.X + item.Width, item.Y + item.Height), (item.X, item.Y + item.Height)
                     })
            {
                var dx = x - centerX;
                var dy = y - centerY;
                var rotatedX = centerX + dx * cos - dy * sin;
                var rotatedY = centerY + dx * sin + dy * cos;
                left = Math.Min(left, rotatedX);
                top = Math.Min(top, rotatedY);
                right = Math.Max(right, rotatedX);
                bottom = Math.Max(bottom, rotatedY);
            }
        }

        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom))
            return new SelectionBounds(0, 0, 1, 1);
        return new SelectionBounds(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    private readonly record struct SelectionBounds(double Left, double Top, double Right, double Bottom);
}
