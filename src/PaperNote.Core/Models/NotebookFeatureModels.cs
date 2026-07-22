namespace PaperNote.Core.Models;

public sealed class PageLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "图层";
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public double Opacity { get; set; } = 1;

    public PageLayer Clone() => new() { Id = Id, Name = Name, IsVisible = IsVisible, IsLocked = IsLocked, Opacity = Opacity };
}

public sealed class AudioRecording
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LocalFilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "本地录音";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public long DurationMilliseconds { get; set; }
    public long FileSize { get; set; }
    public string MimeType { get; set; } = "audio/mp4";
    public List<AudioCue> Cues { get; set; } = [];

    public AudioRecording Clone() => new()
    {
        Id = Id, LocalFilePath = LocalFilePath, DisplayName = DisplayName, CreatedAt = CreatedAt,
        DurationMilliseconds = DurationMilliseconds, FileSize = FileSize, MimeType = MimeType,
        Cues = Cues.Select(cue => cue.Clone()).ToList()
    };
}

public sealed class AudioCue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long OffsetMilliseconds { get; set; }
    public Guid? StrokeId { get; set; }
    public string Label { get; set; } = string.Empty;

    public AudioCue Clone() => new() { Id = Id, OffsetMilliseconds = OffsetMilliseconds, StrokeId = StrokeId, Label = Label };
}


public sealed class DocumentOutlineEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public Guid? TargetPageId { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public int SourcePageNumber { get; set; }
    public bool IsImported { get; set; }

    public DocumentOutlineEntry Clone() => new()
    {
        Id = Id,
        Title = Title,
        Level = Level,
        TargetPageId = TargetPageId,
        SourceFingerprint = SourceFingerprint,
        SourcePageNumber = SourcePageNumber,
        IsImported = IsImported
    };
}

public sealed class PdfPageLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public Guid? TargetPageId { get; set; }
    public int TargetSourcePageNumber { get; set; }
    public string Label { get; set; } = string.Empty;

    public PdfPageLink Clone() => new()
    {
        Id = Id,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        TargetPageId = TargetPageId,
        TargetSourcePageNumber = TargetSourcePageNumber,
        Label = Label
    };
}

public sealed class PageComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public string Color { get; set; } = "#F0B429";
    public double X { get; set; } = 24;
    public double Y { get; set; } = 24;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;

    public PageComment Clone() => new()
    {
        Id = Id,
        Text = Text,
        Color = Color,
        X = X,
        Y = Y,
        CreatedAt = CreatedAt,
        ModifiedAt = ModifiedAt
    };
}
