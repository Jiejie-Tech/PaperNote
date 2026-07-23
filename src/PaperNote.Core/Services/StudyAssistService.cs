using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed record StrokeAudioLocation(AudioRecording Recording, AudioCue Cue);

public static class StudyAssistService
{
    public static PageObject CreateTape(double x = 170, double y = 260, double width = 500, double height = 72, string color = "#F4C542")
        => new()
        {
            Kind = "Shape",
            ShapeKind = "Tape",
            X = x,
            Y = y,
            Width = Math.Max(80, width),
            Height = Math.Max(28, height),
            StrokeColor = color,
            FillColor = color,
            StrokeThickness = 1.5,
            Opacity = .82
        };

    public static IReadOnlyList<PageObject> CreateElement(string kind, double x = 260, double y = 260)
    {
        kind = kind?.Trim() ?? string.Empty;
        return kind switch
        {
            "Important" => [CreateBadge("!", "#C24152", "#FDE8EC", x, y)],
            "Question" => [CreateBadge("？", "#2563EB", "#E8F0FF", x, y)],
            "Check" => [CreateBadge("✓", "#15803D", "#E7F7EC", x, y)],
            "Star" => [CreateBadge("★", "#B87500", "#FFF4CC", x, y)],
            "Divider" => [new PageObject { Kind = "Shape", ShapeKind = "Line", X = x - 120, Y = y + 30, Width = 420, Height = 8, StrokeColor = "#64748B", FillColor = "#00000000", StrokeThickness = 3 }],
            "Formula" => [new PageObject { Kind = "Text", Text = "公式：", X = x - 80, Y = y, Width = 440, Height = 90, FontSize = 28, StrokeColor = "#334155", FillColor = "#F8FAFC" }],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown study element kind.")
        };
    }

    public static StrokeAudioLocation? FindAudioForStroke(NotebookPage page, Guid strokeId)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (strokeId == Guid.Empty) return null;
        return page.AudioRecordings
            .SelectMany(recording => recording.Cues
                .Where(cue => cue.StrokeId == strokeId)
                .Select(cue => new StrokeAudioLocation(recording, cue)))
            .OrderBy(item => item.Cue.OffsetMilliseconds)
            .FirstOrDefault();
    }

    private static PageObject CreateBadge(string text, string stroke, string fill, double x, double y) => new()
    {
        Kind = "Text",
        Text = text,
        X = x,
        Y = y,
        Width = 88,
        Height = 88,
        FontSize = 44,
        StrokeColor = stroke,
        FillColor = fill
    };
}
