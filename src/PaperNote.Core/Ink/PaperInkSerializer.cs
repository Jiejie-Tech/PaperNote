using System.Text.Json;

namespace PaperNote.Core.Ink;

public static class PaperInkSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static string Serialize(PaperInkDocument? document)
    {
        return JsonSerializer.Serialize(document ?? new PaperInkDocument(), Options);
    }

    public static PaperInkDocument Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PaperInkDocument();

        try
        {
            var document = JsonSerializer.Deserialize<PaperInkDocument>(json, Options) ?? new PaperInkDocument();
            Normalize(document);
            return document;
        }
        catch (JsonException)
        {
            return new PaperInkDocument();
        }
    }

    public static void Normalize(PaperInkDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Version = Math.Max(1, document.Version);
        document.Strokes ??= [];
        document.Strokes = document.Strokes.Where(stroke => stroke is not null).ToList();
        var strokeIds = new HashSet<Guid>();
        foreach (var stroke in document.Strokes)
        {
            if (stroke.Id == Guid.Empty || !strokeIds.Add(stroke.Id))
            {
                stroke.Id = Guid.NewGuid();
                strokeIds.Add(stroke.Id);
            }
            stroke.Color = string.IsNullOrWhiteSpace(stroke.Color) ? PaperInkDefaults.PenColor : stroke.Color.Trim();
            stroke.Width = double.IsFinite(stroke.Width) ? Math.Clamp(stroke.Width, 0.25, 128) : PaperInkDefaults.PenWidth;
            stroke.Opacity = double.IsFinite(stroke.Opacity) ? Math.Clamp(stroke.Opacity, 0.01, 1) : 1;
            stroke.Points ??= [];
            stroke.Points = stroke.Points.Where(point => point is not null).ToList();
            foreach (var point in stroke.Points)
            {
                point.X = double.IsFinite(point.X) ? Math.Clamp(point.X, -1_000_000, 1_000_000) : 0;
                point.Y = double.IsFinite(point.Y) ? Math.Clamp(point.Y, -1_000_000, 1_000_000) : 0;
                point.Pressure = double.IsFinite(point.Pressure) ? Math.Clamp(point.Pressure, 0, 1) : 0.5;
                point.TiltX = double.IsFinite(point.TiltX) ? Math.Clamp(point.TiltX, -1, 1) : 0;
                point.TiltY = double.IsFinite(point.TiltY) ? Math.Clamp(point.TiltY, -1, 1) : 0;
                point.TimestampMilliseconds = Math.Max(0, point.TimestampMilliseconds);
            }
        }
    }
}
