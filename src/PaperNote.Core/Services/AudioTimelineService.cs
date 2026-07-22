using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed record AudioTimelineCue(Guid RecordingId, long OffsetMilliseconds, Guid? StrokeId, string Label);

public static class AudioTimelineService
{
    public static AudioRecording AddRecording(NotebookPage page, string localFilePath, long durationMilliseconds, string? displayName = null)
    {
        var recording = new AudioRecording { LocalFilePath = localFilePath, DurationMilliseconds = Math.Max(0, durationMilliseconds), DisplayName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(localFilePath) : displayName.Trim() };
        page.AudioRecordings ??= []; page.AudioRecordings.Add(recording); page.ModifiedAt = DateTimeOffset.Now; return recording;
    }

    public static bool AddCue(AudioRecording recording, long offsetMilliseconds, Guid? strokeId = null, string? label = null)
    {
        offsetMilliseconds = Math.Clamp(offsetMilliseconds, 0, Math.Max(0, recording.DurationMilliseconds));
        if (recording.Cues.Any(cue => cue.OffsetMilliseconds == offsetMilliseconds && cue.StrokeId == strokeId)) return false;
        recording.Cues.Add(new AudioCue { OffsetMilliseconds = offsetMilliseconds, StrokeId = strokeId, Label = label?.Trim() ?? string.Empty });
        recording.Cues = recording.Cues.OrderBy(cue => cue.OffsetMilliseconds).ToList(); return true;
    }

    public static IReadOnlyList<AudioTimelineCue> GetCues(NotebookPage page, long offsetMilliseconds, long tolerance = 250)
        => page.AudioRecordings.SelectMany(recording => recording.Cues.Where(cue => Math.Abs(cue.OffsetMilliseconds - offsetMilliseconds) <= tolerance).Select(cue => new AudioTimelineCue(recording.Id, cue.OffsetMilliseconds, cue.StrokeId, cue.Label))).OrderBy(cue => cue.OffsetMilliseconds).ToArray();
}
