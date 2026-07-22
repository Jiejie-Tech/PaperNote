using System.Text.Json.Serialization;

namespace PaperNote.Core.Ink;

public static class PaperInkDefaults
{
    public const int Version = 1;
    public const string PenColor = "#1D2530";
    public const double PenWidth = 3.2;
    public const double HighlighterWidth = 18;
}

public enum PaperInkTool
{
    Pen,
    Highlighter
}

public sealed class PaperInkDocument
{
    public int Version { get; set; } = PaperInkDefaults.Version;
    public List<PaperInkStroke> Strokes { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Strokes.Count == 0 || Strokes.All(stroke => stroke.Points.Count == 0);

    public PaperInkDocument Clone()
    {
        return new PaperInkDocument
        {
            Version = Version,
            Strokes = Strokes.Select(stroke => stroke.Clone()).ToList()
        };
    }
}

public sealed class PaperInkStroke
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PaperInkTool Tool { get; set; } = PaperInkTool.Pen;
    public string Color { get; set; } = PaperInkDefaults.PenColor;
    public double Width { get; set; } = PaperInkDefaults.PenWidth;
    public double Opacity { get; set; } = 1;
    public bool PressureEnabled { get; set; } = true;
    public Guid? LayerId { get; set; }
    public List<PaperInkPoint> Points { get; set; } = [];

    public PaperInkStroke Clone()
    {
        return new PaperInkStroke
        {
            Id = Id,
            Tool = Tool,
            Color = Color,
            Width = Width,
            Opacity = Opacity,
            PressureEnabled = PressureEnabled,
            LayerId = LayerId,
            Points = Points.Select(point => point.Clone()).ToList()
        };
    }
}

public sealed class PaperInkPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Pressure { get; set; } = 0.5;
    public double TiltX { get; set; }
    public double TiltY { get; set; }
    public long TimestampMilliseconds { get; set; }

    public PaperInkPoint Clone()
    {
        return new PaperInkPoint
        {
            X = X,
            Y = Y,
            Pressure = Pressure,
            TiltX = TiltX,
            TiltY = TiltY,
            TimestampMilliseconds = TimestampMilliseconds
        };
    }
}
