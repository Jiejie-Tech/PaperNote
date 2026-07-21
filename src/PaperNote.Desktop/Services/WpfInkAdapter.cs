using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;

namespace PaperNote.Desktop.Services;

/// <summary>
/// Bridges Windows ISF strokes and PaperNote's cross-platform PaperInk format.
/// ISF remains in notebook files for backwards compatibility while PaperInk is
/// the portable source used by Android and future clients.
/// </summary>
public static class WpfInkAdapter
{
    private static readonly Guid StrokeIdProperty = new("B40A03F0-8C7F-4B3E-A85A-E8D3633D78C7");

    public static PaperInkDocument ToPaperInk(StrokeCollection? strokes)
    {
        var document = new PaperInkDocument();
        if (strokes is null) return document;

        foreach (var stroke in strokes)
        {
            if (stroke.StylusPoints.Count == 0) continue;
            var attributes = stroke.DrawingAttributes;
            var id = ReadStrokeId(stroke);

            var paperStroke = new PaperInkStroke
            {
                Id = id,
                Tool = attributes.IsHighlighter ? PaperInkTool.Highlighter : PaperInkTool.Pen,
                Color = $"#{attributes.Color.R:X2}{attributes.Color.G:X2}{attributes.Color.B:X2}",
                Width = Math.Max(0.25, (attributes.Width + attributes.Height) / 2d),
                Opacity = attributes.IsHighlighter
                    ? 0.35
                    : Math.Clamp(attributes.Color.A / 255d, 0.01, 1),
                PressureEnabled = !attributes.IgnorePressure
            };

            long timestamp = 0;
            foreach (var point in stroke.StylusPoints)
            {
                paperStroke.Points.Add(new PaperInkPoint
                {
                    X = point.X,
                    Y = point.Y,
                    Pressure = Math.Clamp(point.PressureFactor, 0, 1),
                    TimestampMilliseconds = timestamp++
                });
            }
            document.Strokes.Add(paperStroke);
        }

        return document;
    }

    public static StrokeCollection ToStrokeCollection(PaperInkDocument? document)
    {
        var strokes = new StrokeCollection();
        if (document is null) return strokes;

        PaperInkSerializer.Normalize(document);
        foreach (var paperStroke in document.Strokes)
        {
            if (paperStroke.Points.Count == 0) continue;
            var points = new StylusPointCollection();
            foreach (var point in paperStroke.Points)
            {
                points.Add(new StylusPoint(
                    point.X,
                    point.Y,
                    (float)Math.Clamp(paperStroke.PressureEnabled ? point.Pressure : 0.5, 0, 1)));
            }

            var color = ParseColor(paperStroke.Color);
            color.A = (byte)Math.Round(Math.Clamp(paperStroke.Opacity, 0.01, 1) * 255);
            var width = Math.Clamp(paperStroke.Width, 0.25, 128);
            var stroke = new Stroke(points)
            {
                DrawingAttributes = new DrawingAttributes
                {
                    Color = color,
                    Width = width,
                    Height = width,
                    FitToCurve = false,
                    IgnorePressure = !paperStroke.PressureEnabled,
                    IsHighlighter = paperStroke.Tool == PaperInkTool.Highlighter
                }
            };
            stroke.AddPropertyData(
                StrokeIdProperty,
                (paperStroke.Id == Guid.Empty ? Guid.NewGuid() : paperStroke.Id).ToString("D"));
            strokes.Add(stroke);
        }

        return strokes;
    }

    public static StrokeCollection GetPageStrokes(NotebookPage page, bool migrateLegacyInk = false)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Ink is { IsEmpty: false }) return ToStrokeCollection(page.Ink);

        var legacy = PageThumbnailService.Deserialize(page.InkData);
        if (migrateLegacyInk && legacy.Count > 0) page.Ink = ToPaperInk(legacy);
        return legacy;
    }

    private static Guid ReadStrokeId(Stroke stroke)
    {
        if (!stroke.ContainsPropertyData(StrokeIdProperty)) return Guid.NewGuid();

        var value = stroke.GetPropertyData(StrokeIdProperty);
        if (value is Guid guid) return guid;
        return value is string text && Guid.TryParse(text, out var parsed)
            ? parsed
            : Guid.NewGuid();
    }

    private static Color ParseColor(string? value)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color color) return color;
        }
        catch (FormatException)
        {
        }
        return Color.FromRgb(29, 37, 48);
    }
}
