using PaperNote.Core.Ink;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public enum PageSelectionFilter
{
    All,
    Ink,
    Pen,
    Highlighter,
    Objects,
    Text,
    Image,
    Shape
}

public sealed record PageSelectionResult(IReadOnlyList<Guid> StrokeIds, IReadOnlyList<Guid> ObjectIds)
{
    public int Count => StrokeIds.Count + ObjectIds.Count;
}

public sealed record PageSelectionTransferResult(IReadOnlyList<Guid> StrokeIds, IReadOnlyList<Guid> ObjectIds)
{
    public int Count => StrokeIds.Count + ObjectIds.Count;
}

/// <summary>
/// Shared free-form selection, mixed transformation and cross-page transfer rules.
/// All clients use the same geometry so ink and page objects remain aligned.
/// </summary>
public static class PageSelectionService
{
    public static PageSelectionResult SelectByPolygon(NotebookPage page, IReadOnlyList<PaperInkPoint> polygon, PageSelectionFilter filter = PageSelectionFilter.All)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3) return new PageSelectionResult([], []);

        IReadOnlyList<Guid> strokeIds = filter switch
        {
            PageSelectionFilter.All or PageSelectionFilter.Ink => InkSelectionService.SelectByPolygon(page.Ink, polygon),
            PageSelectionFilter.Pen => InkSelectionService.SelectByPolygon(page.Ink, polygon, InkSelectionFilter.Pen),
            PageSelectionFilter.Highlighter => InkSelectionService.SelectByPolygon(page.Ink, polygon, InkSelectionFilter.Highlighter),
            _ => []
        };

        strokeIds = strokeIds.Where(id =>
        {
            var stroke = page.Ink.Strokes.FirstOrDefault(item => item.Id == id);
            return stroke is not null && PageLayerService.IsContentVisible(page, stroke.LayerId);
        }).ToArray();

        IReadOnlyList<Guid> objectIds = filter switch
        {
            PageSelectionFilter.All or PageSelectionFilter.Objects => SelectObjects(page, polygon, null),
            PageSelectionFilter.Text => SelectObjects(page, polygon, "Text"),
            PageSelectionFilter.Image => SelectObjects(page, polygon, "Image"),
            PageSelectionFilter.Shape => SelectObjects(page, polygon, "Shape"),
            _ => []
        };

        return new PageSelectionResult(strokeIds, PageObjectEditingService.ExpandSelection(page, objectIds));
    }

    public static PageObjectBounds? GetCombinedBounds(NotebookPage page, IEnumerable<Guid> strokeIds, IEnumerable<Guid> objectIds)
    {
        ArgumentNullException.ThrowIfNull(page);
        var inkBounds = InkSelectionService.GetBounds(page.Ink, strokeIds);
        var objectBounds = PageObjectEditingService.GetBounds(page, objectIds);
        if (inkBounds is null && objectBounds is null) return null;
        var left = Math.Min(inkBounds?.X ?? double.MaxValue, objectBounds?.X ?? double.MaxValue);
        var top = Math.Min(inkBounds?.Y ?? double.MaxValue, objectBounds?.Y ?? double.MaxValue);
        var right = Math.Max(inkBounds?.Right ?? double.MinValue, objectBounds?.Right ?? double.MinValue);
        var bottom = Math.Max(inkBounds?.Bottom ?? double.MinValue, objectBounds?.Bottom ?? double.MinValue);
        return new PageObjectBounds(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static bool Move(
        NotebookPage page,
        IEnumerable<Guid> strokeIds,
        IEnumerable<Guid> objectIds,
        double deltaX,
        double deltaY,
        double pageWidth = PageObjectEditingService.DefaultPageWidth,
        double pageHeight = PageObjectEditingService.DefaultPageHeight)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY)) return false;
        var strokes = EditableStrokes(page, strokeIds).ToArray();
        var objects = EditableObjects(page, objectIds).ToArray();
        if (strokes.Length + objects.Length == 0) return false;
        var bounds = GetCombinedBounds(page, strokes.Select(item => item.Id), objects.Select(item => item.Id));
        if (bounds is null) return false;
        deltaX = Math.Clamp(deltaX, -bounds.Value.X, pageWidth - bounds.Value.Right);
        deltaY = Math.Clamp(deltaY, -bounds.Value.Y, pageHeight - bounds.Value.Bottom);
        if (Math.Abs(deltaX) < .001 && Math.Abs(deltaY) < .001) return false;

        foreach (var point in strokes.SelectMany(stroke => stroke.Points))
        {
            point.X += deltaX;
            point.Y += deltaY;
        }
        foreach (var item in objects)
        {
            item.X += deltaX;
            item.Y += deltaY;
        }
        Touch(page);
        return true;
    }

    public static bool Resize(
        NotebookPage page,
        IEnumerable<Guid> strokeIds,
        IEnumerable<Guid> objectIds,
        double targetWidth,
        double targetHeight,
        double pageWidth = PageObjectEditingService.DefaultPageWidth,
        double pageHeight = PageObjectEditingService.DefaultPageHeight)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!double.IsFinite(targetWidth) || !double.IsFinite(targetHeight)) return false;
        var strokes = EditableStrokes(page, strokeIds).ToArray();
        var objects = EditableObjects(page, objectIds).ToArray();
        if (strokes.Length + objects.Length == 0) return false;
        var bounds = GetCombinedBounds(page, strokes.Select(item => item.Id), objects.Select(item => item.Id));
        if (bounds is null) return false;
        var source = bounds.Value;

        var minimumScaleX = objects.Length == 0 ? 0 : objects.Max(item => PageObjectEditingService.MinimumObjectSize / Math.Max(item.Width, .001));
        var minimumScaleY = objects.Length == 0 ? 0 : objects.Max(item => PageObjectEditingService.MinimumObjectSize / Math.Max(item.Height, .001));
        var minimumWidth = Math.Max(InkSelectionService.MinimumSelectionSize, source.Width * minimumScaleX);
        var minimumHeight = Math.Max(InkSelectionService.MinimumSelectionSize, source.Height * minimumScaleY);
        targetWidth = Math.Clamp(targetWidth, minimumWidth, Math.Max(minimumWidth, pageWidth - source.X));
        targetHeight = Math.Clamp(targetHeight, minimumHeight, Math.Max(minimumHeight, pageHeight - source.Y));
        var scaleX = targetWidth / Math.Max(source.Width, .001);
        var scaleY = targetHeight / Math.Max(source.Height, .001);
        if (Math.Abs(scaleX - 1) < .001 && Math.Abs(scaleY - 1) < .001) return false;

        foreach (var stroke in strokes)
        {
            foreach (var point in stroke.Points)
            {
                point.X = source.X + (point.X - source.X) * scaleX;
                point.Y = source.Y + (point.Y - source.Y) * scaleY;
            }
            stroke.Width = Math.Clamp(stroke.Width * Math.Sqrt(Math.Abs(scaleX * scaleY)), .25, 128);
        }
        foreach (var item in objects)
        {
            item.X = source.X + (item.X - source.X) * scaleX;
            item.Y = source.Y + (item.Y - source.Y) * scaleY;
            item.Width = Math.Max(PageObjectEditingService.MinimumObjectSize, item.Width * scaleX);
            item.Height = Math.Max(PageObjectEditingService.MinimumObjectSize, item.Height * scaleY);
        }
        Touch(page);
        return true;
    }

    public static bool Rotate(NotebookPage page, IEnumerable<Guid> strokeIds, IEnumerable<Guid> objectIds, double degrees)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!double.IsFinite(degrees) || Math.Abs(degrees) < .001) return false;
        var strokes = EditableStrokes(page, strokeIds).ToArray();
        var objects = EditableObjects(page, objectIds).ToArray();
        if (strokes.Length + objects.Length == 0) return false;
        var bounds = GetCombinedBounds(page, strokes.Select(item => item.Id), objects.Select(item => item.Id));
        if (bounds is null) return false;
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        foreach (var point in strokes.SelectMany(stroke => stroke.Points))
        {
            var x = point.X;
            var y = point.Y;
            RotatePoint(ref x, ref y, bounds.Value.CenterX, bounds.Value.CenterY, cos, sin);
            point.X = x;
            point.Y = y;
        }
        foreach (var item in objects)
        {
            var centerX = item.X + item.Width / 2d;
            var centerY = item.Y + item.Height / 2d;
            RotatePoint(ref centerX, ref centerY, bounds.Value.CenterX, bounds.Value.CenterY, cos, sin);
            item.X = centerX - item.Width / 2d;
            item.Y = centerY - item.Height / 2d;
            item.Rotation = NormalizeRotation(item.Rotation + degrees);
        }
        Touch(page);
        return true;
    }

    public static PageSelectionTransferResult Transfer(
        NotebookPage source,
        NotebookPage target,
        IEnumerable<Guid> strokeIds,
        IEnumerable<Guid> objectIds,
        bool move,
        double offsetX = 24,
        double offsetY = 24)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(source, target) || source.Id == target.Id) return new PageSelectionTransferResult([], []);

        var selectedStrokes = EditableStrokes(source, strokeIds).ToArray();
        var selectedObjects = EditableObjects(source, objectIds).ToArray();
        if (selectedStrokes.Length + selectedObjects.Length == 0) return new PageSelectionTransferResult([], []);
        var targetLayer = PageLayerService.EnsureDefault(target).Id;

        var strokeClones = selectedStrokes.Select(stroke =>
        {
            var clone = stroke.Clone();
            clone.Id = Guid.NewGuid();
            clone.LayerId = targetLayer;
            foreach (var point in clone.Points)
            {
                point.X = Math.Clamp(point.X + offsetX, 0, PageObjectEditingService.DefaultPageWidth);
                point.Y = Math.Clamp(point.Y + offsetY, 0, PageObjectEditingService.DefaultPageHeight);
            }
            return clone;
        }).ToArray();

        var groupMap = selectedObjects
            .Where(item => item.GroupId.HasValue)
            .Select(item => item.GroupId!.Value)
            .Distinct()
            .ToDictionary(id => id, _ => Guid.NewGuid());
        var objectClones = selectedObjects.Select(item =>
        {
            var clone = item.Clone();
            clone.LayerId = targetLayer;
            clone.IsLocked = false;
            clone.GroupId = item.GroupId is Guid groupId ? groupMap[groupId] : null;
            clone.X = Math.Clamp(clone.X + offsetX, 0, Math.Max(0, PageObjectEditingService.DefaultPageWidth - clone.Width));
            clone.Y = Math.Clamp(clone.Y + offsetY, 0, Math.Max(0, PageObjectEditingService.DefaultPageHeight - clone.Height));
            return clone;
        }).ToArray();

        target.Ink.Strokes.AddRange(strokeClones);
        target.Objects.AddRange(objectClones);
        if (move)
        {
            var sourceStrokeIds = selectedStrokes.Select(item => item.Id).ToHashSet();
            var sourceObjectIds = selectedObjects.Select(item => item.Id).ToHashSet();
            source.Ink.Strokes.RemoveAll(item => sourceStrokeIds.Contains(item.Id));
            source.Objects.RemoveAll(item => sourceObjectIds.Contains(item.Id));
            Touch(source);
        }
        Touch(target);
        return new PageSelectionTransferResult(strokeClones.Select(item => item.Id).ToArray(), objectClones.Select(item => item.Id).ToArray());
    }

    private static IReadOnlyList<Guid> SelectObjects(NotebookPage page, IReadOnlyList<PaperInkPoint> polygon, string? kind)
    {
        return page.Objects
            .Where(item => !item.IsHidden && PageLayerService.IsContentVisible(page, item.LayerId))
            .Where(item => kind is null || string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .Where(item => ObjectTouchesPolygon(item, polygon))
            .Select(item => item.Id)
            .ToArray();
    }

    private static IEnumerable<PaperInkStroke> EditableStrokes(NotebookPage page, IEnumerable<Guid> strokeIds)
    {
        var ids = strokeIds.ToHashSet();
        return page.Ink.Strokes.Where(stroke => ids.Contains(stroke.Id) && !PageLayerService.IsContentLocked(page, stroke.LayerId));
    }

    private static IEnumerable<PageObject> EditableObjects(NotebookPage page, IEnumerable<Guid> objectIds)
    {
        var ids = PageObjectEditingService.ExpandSelection(page, objectIds).ToHashSet();
        return page.Objects.Where(item => ids.Contains(item.Id) && !item.IsLocked && !PageLayerService.IsContentLocked(page, item.LayerId));
    }

    private static bool ObjectTouchesPolygon(PageObject item, IReadOnlyList<PaperInkPoint> polygon)
    {
        var corners = RotatedCorners(item);
        if (corners.Any(point => IsPointInPolygon(point.X, point.Y, polygon))) return true;
        if (polygon.Any(point => IsPointInPolygon(point.X, point.Y, corners))) return true;
        for (var a = 0; a < corners.Count; a++)
        {
            var a1 = corners[a];
            var a2 = corners[(a + 1) % corners.Count];
            for (var b = 0; b < polygon.Count; b++)
            {
                var b1 = polygon[b];
                var b2 = polygon[(b + 1) % polygon.Count];
                if (SegmentsIntersect(a1.X, a1.Y, a2.X, a2.Y, b1.X, b1.Y, b2.X, b2.Y)) return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<PaperInkPoint> RotatedCorners(PageObject item)
    {
        var centerX = item.X + item.Width / 2d;
        var centerY = item.Y + item.Height / 2d;
        var radians = item.Rotation * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new[]
        {
            Rotate(item.X, item.Y),
            Rotate(item.X + item.Width, item.Y),
            Rotate(item.X + item.Width, item.Y + item.Height),
            Rotate(item.X, item.Y + item.Height)
        };

        PaperInkPoint Rotate(double x, double y)
        {
            var dx = x - centerX;
            var dy = y - centerY;
            return new PaperInkPoint { X = centerX + dx * cos - dy * sin, Y = centerY + dx * sin + dy * cos };
        }
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

    private static void RotatePoint(ref double x, ref double y, double centerX, double centerY, double cos, double sin)
    {
        var dx = x - centerX;
        var dy = y - centerY;
        x = centerX + dx * cos - dy * sin;
        y = centerY + dx * sin + dy * cos;
    }

    private static double NormalizeRotation(double rotation)
    {
        rotation %= 360;
        return rotation < 0 ? rotation + 360 : rotation;
    }

    private static void Touch(NotebookPage page) => page.ModifiedAt = DateTimeOffset.Now;
}
