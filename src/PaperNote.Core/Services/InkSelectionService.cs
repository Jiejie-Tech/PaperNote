using PaperNote.Core.Ink;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public enum InkSelectionFilter
{
    All,
    Pen,
    Highlighter
}

public readonly record struct InkSelectionBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2d;
    public double CenterY => Y + Height / 2d;
}

/// <summary>
/// Platform-neutral free-form ink selection and transformation commands.
/// The desktop and Android clients use the same geometry and mutation rules so
/// a selection round-trip preserves stroke identity, layer membership and style.
/// </summary>
public static class InkSelectionService
{
    public const double MinimumSelectionSize = 4;

    public static IReadOnlyList<Guid> SelectByPolygon(
        PaperInkDocument document,
        IReadOnlyList<PaperInkPoint> polygon,
        InkSelectionFilter filter = InkSelectionFilter.All,
        Guid? layerId = null,
        double minimumPointRatio = .35)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3) return [];
        minimumPointRatio = Math.Clamp(minimumPointRatio, 0, 1);

        return document.Strokes
            .Where(stroke => MatchesFilter(stroke, filter, layerId))
            .Where(stroke => StrokeTouchesPolygon(stroke, polygon, minimumPointRatio))
            .Select(stroke => stroke.Id)
            .ToArray();
    }

    public static Guid? HitTest(
        PaperInkDocument document,
        double x,
        double y,
        double tolerance = 10,
        InkSelectionFilter filter = InkSelectionFilter.All,
        Guid? layerId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var toleranceSquared = Math.Max(0, tolerance) * Math.Max(0, tolerance);
        for (var strokeIndex = document.Strokes.Count - 1; strokeIndex >= 0; strokeIndex--)
        {
            var stroke = document.Strokes[strokeIndex];
            if (!MatchesFilter(stroke, filter, layerId) || stroke.Points.Count == 0) continue;
            var radius = tolerance + Math.Max(.25, stroke.Width) / 2d;
            var radiusSquared = radius * radius;
            if (stroke.Points.Count == 1)
            {
                if (DistanceSquared(x, y, stroke.Points[0].X, stroke.Points[0].Y) <= Math.Max(toleranceSquared, radiusSquared)) return stroke.Id;
                continue;
            }

            for (var pointIndex = 1; pointIndex < stroke.Points.Count; pointIndex++)
            {
                var a = stroke.Points[pointIndex - 1];
                var b = stroke.Points[pointIndex];
                if (DistanceToSegmentSquared(x, y, a.X, a.Y, b.X, b.Y) <= radiusSquared) return stroke.Id;
            }
        }
        return null;
    }

    public static InkSelectionBounds? GetBounds(PaperInkDocument document, IEnumerable<Guid> strokeIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        var ids = strokeIds.ToHashSet();
        var selected = document.Strokes.Where(stroke => ids.Contains(stroke.Id) && stroke.Points.Count > 0).ToArray();
        if (selected.Length == 0) return null;
        var left = selected.Min(stroke => stroke.Points.Min(point => point.X) - stroke.Width / 2d);
        var top = selected.Min(stroke => stroke.Points.Min(point => point.Y) - stroke.Width / 2d);
        var right = selected.Max(stroke => stroke.Points.Max(point => point.X) + stroke.Width / 2d);
        var bottom = selected.Max(stroke => stroke.Points.Max(point => point.Y) + stroke.Width / 2d);
        return new InkSelectionBounds(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static bool Move(
        NotebookPage page,
        IEnumerable<Guid> strokeIds,
        double deltaX,
        double deltaY,
        double pageWidth = PageObjectEditingService.DefaultPageWidth,
        double pageHeight = PageObjectEditingService.DefaultPageHeight)
    {
        var selected = EditableStrokes(page, strokeIds).ToArray();
        if (selected.Length == 0 || !double.IsFinite(deltaX) || !double.IsFinite(deltaY)) return false;
        var bounds = GetBounds(page.Ink, selected.Select(stroke => stroke.Id));
        if (bounds is null) return false;
        deltaX = Math.Clamp(deltaX, -bounds.Value.X, pageWidth - bounds.Value.Right);
        deltaY = Math.Clamp(deltaY, -bounds.Value.Y, pageHeight - bounds.Value.Bottom);
        if (Math.Abs(deltaX) < .001 && Math.Abs(deltaY) < .001) return false;
        foreach (var point in selected.SelectMany(stroke => stroke.Points)) { point.X += deltaX; point.Y += deltaY; }
        Touch(page);
        return true;
    }

    public static bool Resize(
        NotebookPage page,
        IEnumerable<Guid> strokeIds,
        double targetWidth,
        double targetHeight,
        double pageWidth = PageObjectEditingService.DefaultPageWidth,
        double pageHeight = PageObjectEditingService.DefaultPageHeight)
    {
        var selected = EditableStrokes(page, strokeIds).ToArray();
        if (selected.Length == 0 || !double.IsFinite(targetWidth) || !double.IsFinite(targetHeight)) return false;
        var bounds = GetBounds(page.Ink, selected.Select(stroke => stroke.Id));
        if (bounds is null) return false;
        var source = bounds.Value;
        targetWidth = Math.Clamp(targetWidth, MinimumSelectionSize, Math.Max(MinimumSelectionSize, pageWidth - source.X));
        targetHeight = Math.Clamp(targetHeight, MinimumSelectionSize, Math.Max(MinimumSelectionSize, pageHeight - source.Y));
        var scaleX = targetWidth / Math.Max(source.Width, .001);
        var scaleY = targetHeight / Math.Max(source.Height, .001);
        if (Math.Abs(scaleX - 1) < .001 && Math.Abs(scaleY - 1) < .001) return false;
        foreach (var stroke in selected)
        {
            foreach (var point in stroke.Points)
            {
                point.X = source.X + (point.X - source.X) * scaleX;
                point.Y = source.Y + (point.Y - source.Y) * scaleY;
            }
            stroke.Width = Math.Clamp(stroke.Width * Math.Sqrt(Math.Abs(scaleX * scaleY)), .25, 128);
        }
        Touch(page);
        return true;
    }

    public static bool Rotate(NotebookPage page, IEnumerable<Guid> strokeIds, double degrees)
    {
        var selected = EditableStrokes(page, strokeIds).ToArray();
        if (selected.Length == 0 || !double.IsFinite(degrees) || Math.Abs(degrees) < .001) return false;
        var bounds = GetBounds(page.Ink, selected.Select(stroke => stroke.Id));
        if (bounds is null) return false;
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        foreach (var point in selected.SelectMany(stroke => stroke.Points))
        {
            var dx = point.X - bounds.Value.CenterX;
            var dy = point.Y - bounds.Value.CenterY;
            point.X = bounds.Value.CenterX + dx * cos - dy * sin;
            point.Y = bounds.Value.CenterY + dx * sin + dy * cos;
        }
        Touch(page);
        return true;
    }

    public static bool UpdateStyle(
        NotebookPage page,
        IEnumerable<Guid> strokeIds,
        string? color = null,
        double? width = null,
        double? opacity = null,
        PaperInkTool? tool = null)
    {
        var selected = EditableStrokes(page, strokeIds).ToArray();
        if (selected.Length == 0) return false;
        var changed = false;
        foreach (var stroke in selected)
        {
            if (!string.IsNullOrWhiteSpace(color) && !string.Equals(stroke.Color, color, StringComparison.OrdinalIgnoreCase)) { stroke.Color = color; changed = true; }
            if (width is double requestedWidth && double.IsFinite(requestedWidth))
            {
                var value = Math.Clamp(requestedWidth, .25, 128);
                if (Math.Abs(stroke.Width - value) > .001) { stroke.Width = value; changed = true; }
            }
            if (opacity is double requestedOpacity && double.IsFinite(requestedOpacity))
            {
                var value = Math.Clamp(requestedOpacity, .01, 1);
                if (Math.Abs(stroke.Opacity - value) > .001) { stroke.Opacity = value; changed = true; }
            }
            if (tool is PaperInkTool requestedTool && stroke.Tool != requestedTool) { stroke.Tool = requestedTool; changed = true; }
        }
        if (changed) Touch(page);
        return changed;
    }

    public static int Delete(NotebookPage page, IEnumerable<Guid> strokeIds)
    {
        ArgumentNullException.ThrowIfNull(page);
        var editable = EditableStrokes(page, strokeIds).Select(stroke => stroke.Id).ToHashSet();
        if (editable.Count == 0) return 0;
        var removed = page.Ink.Strokes.RemoveAll(stroke => editable.Contains(stroke.Id));
        if (removed > 0) Touch(page);
        return removed;
    }

    public static IReadOnlyList<Guid> Duplicate(NotebookPage page, IEnumerable<Guid> strokeIds, double offsetX = 24, double offsetY = 24)
    {
        ArgumentNullException.ThrowIfNull(page);
        var selected = EditableStrokes(page, strokeIds).ToArray();
        if (selected.Length == 0) return [];
        var clones = selected.Select(stroke => CloneForTransfer(stroke, stroke.LayerId, offsetX, offsetY)).ToArray();
        page.Ink.Strokes.AddRange(clones);
        Touch(page);
        return clones.Select(stroke => stroke.Id).ToArray();
    }

    public static IReadOnlyList<Guid> Transfer(
        NotebookPage source,
        NotebookPage target,
        IEnumerable<Guid> strokeIds,
        bool move,
        double offsetX = 24,
        double offsetY = 24)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var selected = EditableStrokes(source, strokeIds).ToArray();
        if (selected.Length == 0) return [];
        var targetLayer = PageLayerService.EnsureDefault(target).Id;
        var clones = selected.Select(stroke => CloneForTransfer(stroke, targetLayer, offsetX, offsetY)).ToArray();
        target.Ink.Strokes.AddRange(clones);
        if (move)
        {
            var ids = selected.Select(stroke => stroke.Id).ToHashSet();
            source.Ink.Strokes.RemoveAll(stroke => ids.Contains(stroke.Id));
            Touch(source);
        }
        Touch(target);
        return clones.Select(stroke => stroke.Id).ToArray();
    }

    private static IEnumerable<PaperInkStroke> EditableStrokes(NotebookPage page, IEnumerable<Guid> strokeIds)
    {
        ArgumentNullException.ThrowIfNull(page);
        var ids = strokeIds.ToHashSet();
        return page.Ink.Strokes.Where(stroke => ids.Contains(stroke.Id) && !PageLayerService.IsContentLocked(page, stroke.LayerId));
    }

    private static PaperInkStroke CloneForTransfer(PaperInkStroke stroke, Guid? layerId, double offsetX, double offsetY)
    {
        var clone = stroke.Clone();
        clone.Id = Guid.NewGuid();
        clone.LayerId = layerId;
        foreach (var point in clone.Points) { point.X += offsetX; point.Y += offsetY; }
        return clone;
    }

    private static bool MatchesFilter(PaperInkStroke stroke, InkSelectionFilter filter, Guid? layerId)
    {
        if (layerId.HasValue && stroke.LayerId != layerId) return false;
        return filter switch
        {
            InkSelectionFilter.Pen => stroke.Tool == PaperInkTool.Pen,
            InkSelectionFilter.Highlighter => stroke.Tool == PaperInkTool.Highlighter,
            _ => true
        };
    }

    private static bool StrokeTouchesPolygon(PaperInkStroke stroke, IReadOnlyList<PaperInkPoint> polygon, double minimumPointRatio)
    {
        if (stroke.Points.Count == 0) return false;
        var insideCount = stroke.Points.Count(point => IsPointInPolygon(point.X, point.Y, polygon));
        if (insideCount > 0 && (double)insideCount / stroke.Points.Count >= minimumPointRatio) return true;
        if (stroke.Points.Count == 1) return insideCount == 1;
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            var a = stroke.Points[i - 1];
            var b = stroke.Points[i];
            for (var edge = 0; edge < polygon.Count; edge++)
            {
                var c = polygon[edge];
                var d = polygon[(edge + 1) % polygon.Count];
                if (SegmentsIntersect(a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y)) return true;
            }
        }
        return false;
    }

    private static bool IsPointInPolygon(double x, double y, IReadOnlyList<PaperInkPoint> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = i == 0 ? polygon.Count - 1 : i - 1;
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > y) == (b.Y > y)) continue;
            var crossingX = (b.X - a.X) * (y - a.Y) / ((b.Y - a.Y) == 0 ? double.Epsilon : b.Y - a.Y) + a.X;
            if (x < crossingX) inside = !inside;
        }
        return inside;
    }

    private static bool SegmentsIntersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        static double Cross(double x1, double y1, double x2, double y2, double x3, double y3)
            => (x2 - x1) * (y3 - y1) - (y2 - y1) * (x3 - x1);
        var d1 = Cross(ax, ay, bx, by, cx, cy);
        var d2 = Cross(ax, ay, bx, by, dx, dy);
        var d3 = Cross(cx, cy, dx, dy, ax, ay);
        var d4 = Cross(cx, cy, dx, dy, bx, by);
        const double epsilon = .000001;
        if (((d1 > epsilon && d2 < -epsilon) || (d1 < -epsilon && d2 > epsilon)) &&
            ((d3 > epsilon && d4 < -epsilon) || (d3 < -epsilon && d4 > epsilon))) return true;
        return Math.Abs(d1) <= epsilon && OnSegment(ax, ay, bx, by, cx, cy) ||
               Math.Abs(d2) <= epsilon && OnSegment(ax, ay, bx, by, dx, dy) ||
               Math.Abs(d3) <= epsilon && OnSegment(cx, cy, dx, dy, ax, ay) ||
               Math.Abs(d4) <= epsilon && OnSegment(cx, cy, dx, dy, bx, by);
    }

    private static bool OnSegment(double ax, double ay, double bx, double by, double px, double py)
        => px >= Math.Min(ax, bx) - .000001 && px <= Math.Max(ax, bx) + .000001 &&
           py >= Math.Min(ay, by) - .000001 && py <= Math.Max(ay, by) + .000001;

    private static double DistanceToSegmentSquared(double x, double y, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon) return DistanceSquared(x, y, x1, y1);
        var t = Math.Clamp(((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy), 0, 1);
        return DistanceSquared(x, y, x1 + t * dx, y1 + t * dy);
    }

    private static double DistanceSquared(double x1, double y1, double x2, double y2)
        => (x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1);

    private static void Touch(NotebookPage page) => page.ModifiedAt = DateTimeOffset.Now;
}
