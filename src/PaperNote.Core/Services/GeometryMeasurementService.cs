using PaperNote.Core.Ink;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed record GeometryMeasurement(
    double WidthMillimeters,
    double HeightMillimeters,
    double DirectDistanceMillimeters,
    double PathLengthMillimeters,
    double PerimeterMillimeters,
    double AreaSquareMillimeters,
    double AngleDegrees,
    int SegmentCount,
    bool IsClosed)
{
    public string ToDisplayText() =>
        $"宽 {WidthMillimeters:0.0} mm · 高 {HeightMillimeters:0.0} mm\n" +
        $"端点距离 {DirectDistanceMillimeters:0.0} mm · 路径长度 {PathLengthMillimeters:0.0} mm\n" +
        $"方向角 {AngleDegrees:0.0}° · 线段 {SegmentCount} 段" +
        (PerimeterMillimeters > 0 ? $"\n周长 {PerimeterMillimeters:0.0} mm" : string.Empty) +
        (AreaSquareMillimeters > 0 ? $" · 面积 {AreaSquareMillimeters:0.0} mm²" : string.Empty);
}

/// <summary>Offline page geometry measurement and ruler/construction helpers. PaperNote pages use 4 px per millimeter.</summary>
public static class GeometryMeasurementService
{
    public const double PixelsPerMillimeter = 4;

    public static GeometryMeasurement? MeasureSelection(
        NotebookPage page,
        IEnumerable<Guid> strokeIds,
        IEnumerable<Guid> objectIds)
    {
        ArgumentNullException.ThrowIfNull(page);
        var selectedStrokeIds = strokeIds.ToHashSet();
        var selectedObjectIds = objectIds.ToHashSet();
        var strokes = page.Ink.Strokes.Where(stroke => selectedStrokeIds.Contains(stroke.Id) && stroke.Points.Count > 0).ToArray();
        var objects = page.Objects.Where(item => selectedObjectIds.Contains(item.Id)).ToArray();
        if (strokes.Length == 0 && objects.Length == 0) return null;

        var points = new List<PaperInkPoint>();
        var path = 0d;
        var segments = 0;
        var perimeter = 0d;
        var area = 0d;
        var closed = false;
        foreach (var stroke in strokes)
        {
            points.AddRange(stroke.Points);
            for (var i = 1; i < stroke.Points.Count; i++)
            {
                path += Distance(stroke.Points[i - 1], stroke.Points[i]);
                segments++;
            }
            if (stroke.Points.Count >= 3 && Distance(stroke.Points[0], stroke.Points[^1]) <= Math.Max(12, stroke.Width * 2))
            {
                closed = true;
                perimeter += PolylineLength(stroke.Points);
                area += Math.Abs(SignedArea(stroke.Points));
            }
        }
        foreach (var item in objects)
        {
            var corners = RotatedCorners(item);
            points.AddRange(corners.Select(point => new PaperInkPoint { X = point.X, Y = point.Y }));
            if (item.Kind == "Shape" && item.ShapeKind is not "Line" and not "Arrow")
            {
                closed = true;
                perimeter += item.ShapeKind == "Ellipse"
                    ? EllipsePerimeter(item.Width / 2, item.Height / 2)
                    : 2 * (item.Width + item.Height);
                area += item.ShapeKind == "Ellipse"
                    ? Math.PI * item.Width * item.Height / 4
                    : item.Width * item.Height;
            }
            else
            {
                path += Math.Sqrt(item.Width * item.Width + item.Height * item.Height);
                segments++;
            }
        }

        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        var first = strokes.FirstOrDefault()?.Points.FirstOrDefault() ?? points[0];
        var last = strokes.LastOrDefault()?.Points.LastOrDefault() ?? points[^1];
        var direct = Distance(first, last);
        var angle = NormalizeHalfTurn(Math.Atan2(last.Y - first.Y, last.X - first.X) * 180 / Math.PI);
        if (path == 0) path = direct;
        return new GeometryMeasurement(
            (right - left) / PixelsPerMillimeter,
            (bottom - top) / PixelsPerMillimeter,
            direct / PixelsPerMillimeter,
            path / PixelsPerMillimeter,
            perimeter / PixelsPerMillimeter,
            area / (PixelsPerMillimeter * PixelsPerMillimeter),
            angle,
            segments,
            closed);
    }

    public static IReadOnlyList<PaperInkStroke> CreateRuler(
        double x,
        double y,
        double lengthMillimeters = 100,
        double angleDegrees = 0,
        string color = "#315CBB",
        double width = 2,
        Guid? layerId = null)
    {
        lengthMillimeters = Math.Clamp(lengthMillimeters, 20, 200);
        var length = lengthMillimeters * PixelsPerMillimeter;
        var angle = angleDegrees * Math.PI / 180;
        var ux = Math.Cos(angle); var uy = Math.Sin(angle);
        var nx = -uy; var ny = ux;
        var strokes = new List<PaperInkStroke>
        {
            Line(x, y, x + ux * length, y + uy * length, color, width, layerId)
        };
        var tickCount = (int)Math.Round(lengthMillimeters / 5);
        for (var tick = 0; tick <= tickCount; tick++)
        {
            var offset = tick * 5 * PixelsPerMillimeter;
            var major = tick % 2 == 0;
            var tickLength = (major ? 5 : 3) * PixelsPerMillimeter;
            var sx = x + ux * offset; var sy = y + uy * offset;
            strokes.Add(Line(sx, sy, sx + nx * tickLength, sy + ny * tickLength, color, major ? width : Math.Max(1, width * .75), layerId));
        }
        return strokes;
    }

    public static PaperInkStroke CreateParallelLine(PaperInkStroke reference, double offsetMillimeters, string? color = null, Guid? layerId = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Points.Count < 2) throw new InvalidOperationException("至少需要一条包含两个点的参考线。");
        var first = reference.Points[0]; var last = reference.Points[^1];
        var dx = last.X - first.X; var dy = last.Y - first.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) throw new InvalidOperationException("参考线太短，无法构造平行线。");
        var offset = offsetMillimeters * PixelsPerMillimeter;
        var nx = -dy / length; var ny = dx / length;
        return Line(first.X + nx * offset, first.Y + ny * offset, last.X + nx * offset, last.Y + ny * offset, color ?? reference.Color, reference.Width, layerId ?? reference.LayerId);
    }

    public static PaperInkStroke CreatePerpendicularLine(PaperInkStroke reference, double lengthMillimeters = 50, string? color = null, Guid? layerId = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Points.Count < 2) throw new InvalidOperationException("至少需要一条包含两个点的参考线。");
        var first = reference.Points[0]; var last = reference.Points[^1];
        var dx = last.X - first.X; var dy = last.Y - first.Y;
        var referenceLength = Math.Sqrt(dx * dx + dy * dy);
        if (referenceLength < 1) throw new InvalidOperationException("参考线太短，无法构造垂线。");
        var half = Math.Clamp(lengthMillimeters, 10, 200) * PixelsPerMillimeter / 2;
        var cx = (first.X + last.X) / 2; var cy = (first.Y + last.Y) / 2;
        var nx = -dy / referenceLength; var ny = dx / referenceLength;
        return Line(cx - nx * half, cy - ny * half, cx + nx * half, cy + ny * half, color ?? reference.Color, reference.Width, layerId ?? reference.LayerId);
    }

    public static IReadOnlyList<PaperInkStroke> CreateAngleGuide(
        double x,
        double y,
        double angleDegrees,
        double radiusMillimeters = 40,
        string color = "#315CBB",
        double width = 2,
        Guid? layerId = null)
    {
        angleDegrees = Math.Clamp(angleDegrees, 1, 179);
        var radius = Math.Clamp(radiusMillimeters, 10, 100) * PixelsPerMillimeter;
        var radians = angleDegrees * Math.PI / 180;
        var strokes = new List<PaperInkStroke>
        {
            Line(x, y, x + radius, y, color, width, layerId),
            Line(x, y, x + Math.Cos(radians) * radius, y - Math.Sin(radians) * radius, color, width, layerId)
        };
        var arc = new PaperInkStroke { Tool = PaperInkTool.Pen, Color = color, Width = width, PressureEnabled = false, LayerId = layerId };
        for (var i = 0; i <= 24; i++)
        {
            var a = radians * i / 24;
            arc.Points.Add(new PaperInkPoint { X = x + Math.Cos(a) * radius * .32, Y = y - Math.Sin(a) * radius * .32, Pressure = .5 });
        }
        strokes.Add(arc);
        return strokes;
    }

    private static PaperInkStroke Line(double x1, double y1, double x2, double y2, string color, double width, Guid? layerId) => new()
    {
        Tool = PaperInkTool.Pen,
        Color = color,
        Width = Math.Clamp(width, 1, 20),
        PressureEnabled = false,
        LayerId = layerId,
        Points = [new PaperInkPoint { X = x1, Y = y1, Pressure = .5 }, new PaperInkPoint { X = x2, Y = y2, Pressure = .5 }]
    };

    private static IReadOnlyList<(double X, double Y)> RotatedCorners(PageObject item)
    {
        var cx = item.X + item.Width / 2; var cy = item.Y + item.Height / 2;
        var radians = item.Rotation * Math.PI / 180;
        var cos = Math.Cos(radians); var sin = Math.Sin(radians);
        return new[] { (-item.Width / 2, -item.Height / 2), (item.Width / 2, -item.Height / 2), (item.Width / 2, item.Height / 2), (-item.Width / 2, item.Height / 2) }
            .Select(point => (cx + point.Item1 * cos - point.Item2 * sin, cy + point.Item1 * sin + point.Item2 * cos)).ToArray();
    }
    private static double PolylineLength(IReadOnlyList<PaperInkPoint> points) { var total = 0d; for (var i = 1; i < points.Count; i++) total += Distance(points[i - 1], points[i]); return total; }
    private static double SignedArea(IReadOnlyList<PaperInkPoint> points) { var area = 0d; for (var i = 0; i < points.Count - 1; i++) area += points[i].X * points[i + 1].Y - points[i + 1].X * points[i].Y; return area / 2; }
    private static double EllipsePerimeter(double a, double b) => Math.PI * (3 * (a + b) - Math.Sqrt((3 * a + b) * (a + 3 * b)));
    private static double Distance(PaperInkPoint a, PaperInkPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static double NormalizeHalfTurn(double degrees) { degrees %= 180; if (degrees < 0) degrees += 180; return degrees; }
}
