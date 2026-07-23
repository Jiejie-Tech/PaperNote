using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public sealed record AudioTimelineCue(Guid RecordingId, long OffsetMilliseconds, Guid? StrokeId, string Label);

public static class AudioTimelineService
{
    public static AudioRecording AddRecording(NotebookPage page, string localFilePath, long durationMilliseconds, string? displayName = null, long fileSize = 0, string? mimeType = null)
    {
        var recording = new AudioRecording
        {
            LocalFilePath = localFilePath,
            DurationMilliseconds = Math.Max(0, durationMilliseconds),
            FileSize = Math.Max(0, fileSize),
            MimeType = string.IsNullOrWhiteSpace(mimeType) ? "audio/mp4" : mimeType.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(localFilePath) : displayName.Trim()
        };
        page.AudioRecordings ??= [];
        page.AudioRecordings.Add(recording);
        page.ModifiedAt = DateTimeOffset.Now;
        return recording;
    }

    public static bool AddCue(AudioRecording recording, long offsetMilliseconds, Guid? strokeId = null, string? label = null)
    {
        offsetMilliseconds = Math.Max(0, offsetMilliseconds);
        if (recording.DurationMilliseconds > 0)
            offsetMilliseconds = Math.Min(offsetMilliseconds, recording.DurationMilliseconds);
        if (recording.Cues.Any(cue => Math.Abs(cue.OffsetMilliseconds - offsetMilliseconds) < 100 && cue.StrokeId == strokeId)) return false;
        recording.Cues.Add(new AudioCue { OffsetMilliseconds = offsetMilliseconds, StrokeId = strokeId, Label = label?.Trim() ?? string.Empty });
        recording.Cues = recording.Cues.OrderBy(cue => cue.OffsetMilliseconds).ToList();
        return true;
    }

    public static void UpdateRecording(AudioRecording recording, long durationMilliseconds, long fileSize)
    {
        recording.DurationMilliseconds = Math.Max(0, durationMilliseconds);
        recording.FileSize = Math.Max(0, fileSize);
        foreach (var cue in recording.Cues)
            cue.OffsetMilliseconds = Math.Clamp(cue.OffsetMilliseconds, 0, recording.DurationMilliseconds);
    }

    public static bool RemoveRecording(NotebookPage page, Guid recordingId)
    {
        var removed = page.AudioRecordings.RemoveAll(item => item.Id == recordingId) > 0;
        if (removed) page.ModifiedAt = DateTimeOffset.Now;
        return removed;
    }

    public static IReadOnlyList<AudioTimelineCue> GetCues(NotebookPage page, long offsetMilliseconds, long tolerance = 250)
        => page.AudioRecordings
            .SelectMany(recording => recording.Cues
                .Where(cue => Math.Abs(cue.OffsetMilliseconds - offsetMilliseconds) <= Math.Max(0, tolerance))
                .Select(cue => new AudioTimelineCue(recording.Id, cue.OffsetMilliseconds, cue.StrokeId, cue.Label)))
            .OrderBy(cue => cue.OffsetMilliseconds)
            .ToArray();


    public static Guid? GetActiveStrokeId(AudioRecording recording, long offsetMilliseconds, long holdMilliseconds = 1400)
    {
        ArgumentNullException.ThrowIfNull(recording);
        offsetMilliseconds = Math.Max(0, offsetMilliseconds);
        holdMilliseconds = Math.Max(100, holdMilliseconds);
        var cue = recording.Cues
            .Where(item => item.StrokeId.HasValue && item.OffsetMilliseconds <= offsetMilliseconds)
            .OrderByDescending(item => item.OffsetMilliseconds)
            .FirstOrDefault();
        return cue is not null && offsetMilliseconds - cue.OffsetMilliseconds <= holdMilliseconds ? cue.StrokeId : null;
    }

    public static double GetPlaybackProgress(AudioRecording recording, long offsetMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return recording.DurationMilliseconds <= 0
            ? 0
            : Math.Clamp(offsetMilliseconds / (double)recording.DurationMilliseconds, 0, 1);
    }

    public static string FormatDuration(long milliseconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }
}
