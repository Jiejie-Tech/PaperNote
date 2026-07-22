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
    private static readonly Guid LayerIdProperty = new("36259A59-9B8F-49CB-9562-CBE7F4A84ED0");
    private static readonly Guid OpacityProperty = new("BE57B762-9882-442B-AE3E-3463A4E2095B");

    public static PaperInkDocument ToPaperInk(StrokeCollection? strokes)
    {
        var document = new PaperInkDocument();
        if (strokes is null) return document;

        foreach (var stroke in strokes)
        {
            if (stroke.StylusPoints.Count == 0) continue;
            var attributes = stroke.DrawingAttributes;
            var id = GetStrokeId(stroke);

            var paperStroke = new PaperInkStroke
            {
                Id = id,
                Tool = attributes.IsHighlighter ? PaperInkTool.Highlighter : PaperInkTool.Pen,
                Color = $"#{attributes.Color.R:X2}{attributes.Color.G:X2}{attributes.Color.B:X2}",
                Width = Math.Max(0.25, (attributes.Width + attributes.Height) / 2d),
                Opacity = GetOpacity(stroke),
                PressureEnabled = !attributes.IgnorePressure,
                LayerId = GetLayerId(stroke)
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
            SetStrokeId(stroke, paperStroke.Id == Guid.Empty ? Guid.NewGuid() : paperStroke.Id);
            SetLayerId(stroke, paperStroke.LayerId);
            SetOpacity(stroke, paperStroke.Opacity);
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

    public static Guid GetStrokeId(Stroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if (TryReadGuid(stroke, StrokeIdProperty, out var id)) return id;
        id = Guid.NewGuid();
        SetStrokeId(stroke, id);
        return id;
    }

    public static Guid? GetLayerId(Stroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        return TryReadGuid(stroke, LayerIdProperty, out var id) ? id : null;
    }

    public static void SetLayerId(Stroke stroke, Guid? layerId)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if (stroke.ContainsPropertyData(LayerIdProperty)) stroke.RemovePropertyData(LayerIdProperty);
        if (layerId is Guid id && id != Guid.Empty) stroke.AddPropertyData(LayerIdProperty, id.ToString("D"));
    }

    public static double GetOpacity(Stroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if (stroke.ContainsPropertyData(OpacityProperty))
        {
            var value = stroke.GetPropertyData(OpacityProperty);
            if (value is double number && double.IsFinite(number)) return Math.Clamp(number, .01, 1);
            if (value is string text && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
                return Math.Clamp(number, .01, 1);
        }
        return stroke.DrawingAttributes.IsHighlighter
            ? .35
            : Math.Clamp(stroke.DrawingAttributes.Color.A / 255d, .01, 1);
    }

    public static void SetOpacity(Stroke stroke, double opacity)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        opacity = Math.Clamp(double.IsFinite(opacity) ? opacity : 1, .01, 1);
        if (stroke.ContainsPropertyData(OpacityProperty)) stroke.RemovePropertyData(OpacityProperty);
        stroke.AddPropertyData(OpacityProperty, opacity);
        var color = stroke.DrawingAttributes.Color;
        color.A = (byte)Math.Round(opacity * 255);
        stroke.DrawingAttributes.Color = color;
    }

    private static void SetStrokeId(Stroke stroke, Guid id)
    {
        if (stroke.ContainsPropertyData(StrokeIdProperty)) stroke.RemovePropertyData(StrokeIdProperty);
        stroke.AddPropertyData(StrokeIdProperty, (id == Guid.Empty ? Guid.NewGuid() : id).ToString("D"));
    }

    private static bool TryReadGuid(Stroke stroke, Guid propertyId, out Guid id)
    {
        id = Guid.Empty;
        if (!stroke.ContainsPropertyData(propertyId)) return false;
        var value = stroke.GetPropertyData(propertyId);
        if (value is Guid guid && guid != Guid.Empty)
        {
            id = guid;
            return true;
        }
        return value is string text && Guid.TryParse(text, out id) && id != Guid.Empty;
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
