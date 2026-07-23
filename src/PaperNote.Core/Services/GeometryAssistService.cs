using PaperNote.Core.Ink;

namespace PaperNote.Core.Services;

public enum GeometryAssistResult
{
    None,
    Line,
    Rectangle,
    Ellipse
}

/// <summary>Normalizes deliberate rough strokes into clean offline geometry without changing their ink style.</summary>
public static class GeometryAssistService
{
    public static GeometryAssistResult NormalizeStroke(PaperInkStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if (stroke.Points.Count < 2 || stroke.Tool != PaperInkTool.Pen) return GeometryAssistResult.None;

        var points = stroke.Points;
        var bounds = Bounds(points);
        var diagonal = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
        if (diagonal < 20) return GeometryAssistResult.None;

        var first = points[0];
        var last = points[^1];
        var closure = Distance(first, last);
        if (points.Count >= 8 && closure <= Math.Max(24, diagonal * .2))
        {
            var rectangleScore = RectangleScore(points, bounds);
            var ellipseScore = EllipseScore(points, bounds);
            if (rectangleScore <= .16 && rectangleScore + .025 < ellipseScore)
            {
                Replace(stroke, RectanglePoints(bounds, AveragePressure(points)));
                return GeometryAssistResult.Rectangle;
            }
            if (ellipseScore <= .22)
            {
                Replace(stroke, EllipsePoints(bounds, AveragePressure(points)));
                return GeometryAssistResult.Ellipse;
            }
        }

        var pathLength = 0d;
        for (var index = 1; index < points.Count; index++) pathLength += Distance(points[index - 1], points[index]);
        var chord = Distance(first, last);
        if (chord < 24 || pathLength <= 0 || chord / pathLength < .88) return GeometryAssistResult.None;

        var angle = Math.Atan2(last.Y - first.Y, last.X - first.X);
        var step = Math.PI / 4;
        var snapped = Math.Round(angle / step) * step;
        if (Math.Abs(NormalizeAngle(angle - snapped)) > 12 * Math.PI / 180) return GeometryAssistResult.None;
        var pressure = AveragePressure(points);
        var end = new PaperInkPoint
        {
            X = first.X + Math.Cos(snapped) * chord,
            Y = first.Y + Math.Sin(snapped) * chord,
            Pressure = pressure,
            TimestampMilliseconds = last.TimestampMilliseconds
        };
        Replace(stroke, [CloneAt(first, first.X, first.Y, pressure), end]);
        return GeometryAssistResult.Line;
    }

    private static (double X, double Y, double Width, double Height) Bounds(IReadOnlyList<PaperInkPoint> points)
    {
        var left = points.Min(point => point.X); var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X); var bottom = points.Max(point => point.Y);
        return (left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static double RectangleScore(IReadOnlyList<PaperInkPoint> points, (double X, double Y, double Width, double Height) b)
    {
        var scale = Math.Max(1, Math.Min(b.Width, b.Height));
        return points.Average(point => Math.Min(Math.Min(Math.Abs(point.X - b.X), Math.Abs(point.X - (b.X + b.Width))), Math.Min(Math.Abs(point.Y - b.Y), Math.Abs(point.Y - (b.Y + b.Height))))) / scale;
    }

    private static double EllipseScore(IReadOnlyList<PaperInkPoint> points, (double X, double Y, double Width, double Height) b)
    {
        var rx = b.Width / 2; var ry = b.Height / 2; var cx = b.X + rx; var cy = b.Y + ry;
        return points.Average(point => Math.Abs(Math.Sqrt(Math.Pow((point.X - cx) / rx, 2) + Math.Pow((point.Y - cy) / ry, 2)) - 1));
    }

    private static List<PaperInkPoint> RectanglePoints((double X, double Y, double Width, double Height) b, double pressure)
    {
        var right = b.X + b.Width; var bottom = b.Y + b.Height;
        return [Point(b.X,b.Y,pressure),Point(right,b.Y,pressure),Point(right,bottom,pressure),Point(b.X,bottom,pressure),Point(b.X,b.Y,pressure)];
    }

    private static List<PaperInkPoint> EllipsePoints((double X, double Y, double Width, double Height) b, double pressure)
    {
        var result = new List<PaperInkPoint>(33); var rx=b.Width/2; var ry=b.Height/2; var cx=b.X+rx; var cy=b.Y+ry;
        for (var index=0; index<=32; index++) { var angle=Math.PI*2*index/32; result.Add(Point(cx+Math.Cos(angle)*rx,cy+Math.Sin(angle)*ry,pressure)); }
        return result;
    }

    private static PaperInkPoint Point(double x,double y,double pressure) => new() { X=x,Y=y,Pressure=pressure };
    private static PaperInkPoint CloneAt(PaperInkPoint source,double x,double y,double pressure) => new() { X=x,Y=y,Pressure=pressure,TiltX=source.TiltX,TiltY=source.TiltY,TimestampMilliseconds=source.TimestampMilliseconds };
    private static double AveragePressure(IEnumerable<PaperInkPoint> points) => Math.Clamp(points.Average(point => point.Pressure), .05, 1);
    private static double Distance(PaperInkPoint a, PaperInkPoint b) => Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    private static double NormalizeAngle(double angle) { while(angle>Math.PI) angle-=Math.PI*2; while(angle< -Math.PI) angle+=Math.PI*2; return angle; }
    private static void Replace(PaperInkStroke stroke, List<PaperInkPoint> points) { stroke.Points=points; stroke.PressureEnabled=false; }
}
