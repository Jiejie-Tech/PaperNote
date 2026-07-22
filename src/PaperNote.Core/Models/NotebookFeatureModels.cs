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
    public List<AudioCue> Cues { get; set; } = [];

    public AudioRecording Clone() => new()
    {
        Id = Id, LocalFilePath = LocalFilePath, DisplayName = DisplayName, CreatedAt = CreatedAt,
        DurationMilliseconds = DurationMilliseconds, Cues = Cues.Select(cue => cue.Clone()).ToList()
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
