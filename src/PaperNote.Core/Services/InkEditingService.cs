using PaperNote.Core.Ink;

namespace PaperNote.Core.Services;

/// <summary>Shared offline ink editing helpers for smoothing and partial erasing.</summary>
public static class InkEditingService
{
    public static int ErasePartial(PaperInkDocument document, double x, double y, double radius)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(radius) || radius <= 0) return 0;
        var output = new List<PaperInkStroke>(document.Strokes.Count);
        var changed = 0;
        foreach (var stroke in document.Strokes)
        {
            if (stroke.Points.Count == 0) continue;
            var fragments = SplitOutsideCircle(stroke, x, y, radius);
            if (fragments.Count == 1 && fragments[0].Points.Count == stroke.Points.Count)
            {
                output.Add(stroke);
                continue;
            }
            changed++;
            output.AddRange(fragments.Where(fragment => fragment.Points.Count >= 2));
        }
        if (changed > 0) document.Strokes = output;
        return changed;
    }

    public static bool SmoothStroke(PaperInkStroke stroke, double strength = .45)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if (stroke.Points.Count < 3) return false;
        strength = Math.Clamp(strength, 0, 1);
        var original = stroke.Points.Select(point => point.Clone()).ToArray();
        for (var i = 1; i < stroke.Points.Count - 1; i++)
        {
            var previous = original[i - 1];
            var current = original[i];
            var next = original[i + 1];
            var averageX = (previous.X + current.X + next.X) / 3;
            var averageY = (previous.Y + current.Y + next.Y) / 3;
            current.X = current.X + (averageX - current.X) * strength;
            current.Y = current.Y + (averageY - current.Y) * strength;
            stroke.Points[i] = current;
        }
        return true;
    }

    public static double EstimateLength(PaperInkStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        var length = 0d;
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            var a = stroke.Points[i - 1];
            var b = stroke.Points[i];
            length += Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        }
        return length;
    }

    private static List<PaperInkStroke> SplitOutsideCircle(PaperInkStroke source, double x, double y, double radius)
    {
        var result = new List<PaperInkStroke>();
        List<PaperInkPoint>? current = null;
        foreach (var point in source.Points)
        {
            var outside = Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2)) > radius;
            if (outside)
            {
                current ??= [];
                current.Add(point.Clone());
            }
            else if (current is not null)
            {
                if (current.Count >= 2) result.Add(CloneWithPoints(source, current));
                current = null;
            }
        }
        if (current is not null && current.Count >= 2) result.Add(CloneWithPoints(source, current));
        if (result.Count == 0 && source.Points.All(point => Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2)) > radius))
            result.Add(source);
        return result;
    }

    private static PaperInkStroke CloneWithPoints(PaperInkStroke source, List<PaperInkPoint> points)
        => new()
        {
            Id = Guid.NewGuid(), Tool = source.Tool, Color = source.Color, Width = source.Width,
            Opacity = source.Opacity, PressureEnabled = source.PressureEnabled, Points = points
        };
}
