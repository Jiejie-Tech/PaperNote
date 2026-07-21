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
        document.Version = Math.Max(1, document.Version);
        document.Strokes ??= [];
        foreach (var stroke in document.Strokes)
        {
            stroke.Color = string.IsNullOrWhiteSpace(stroke.Color) ? PaperInkDefaults.PenColor : stroke.Color;
            stroke.Width = Math.Clamp(stroke.Width, 0.25, 128);
            stroke.Opacity = Math.Clamp(stroke.Opacity, 0.01, 1);
            stroke.Points ??= [];
            foreach (var point in stroke.Points)
            {
                point.Pressure = Math.Clamp(point.Pressure, 0, 1);
                point.TiltX = Math.Clamp(point.TiltX, -1, 1);
                point.TiltY = Math.Clamp(point.TiltY, -1, 1);
            }
        }
    }
}
