using System.Diagnostics;
using Android.Media;
using Android.OS;

namespace PaperNote.Mobile.Services;

public sealed class MobileAudioService : IDisposable
{
    private MediaRecorder? _recorder;
    private MediaPlayer? _player;
    private readonly Stopwatch _recordingClock = new();
    private string? _recordingPath;

    public bool IsRecording => _recorder is not null;
    public bool HasPlayback => _player is not null;
    public bool IsPlaying => _player?.IsPlaying == true;
    public long RecordingElapsedMilliseconds => _recordingClock.ElapsedMilliseconds;
    public long PlaybackPositionMilliseconds => _player?.CurrentPosition ?? 0;
    public long PlaybackDurationMilliseconds => _player?.Duration ?? 0;
    public float RecordingAmplitude
    {
        get
        {
            try
            {
                var raw = _recorder?.MaxAmplitude ?? 0;
                return Math.Clamp((float)Math.Sqrt(raw / 32767d), 0f, 1f);
            }
            catch { return 0; }
        }
    }

    public async Task<bool> EnsurePermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Microphone>();
        return status == PermissionStatus.Granted;
    }

    public void StartRecording(string filePath)
    {
        StopPlayback();
        if (IsRecording) throw new InvalidOperationException("A recording is already active.");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

#pragma warning disable CA1422
        var recorder = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new MediaRecorder(Android.App.Application.Context)
            : new MediaRecorder();
#pragma warning restore CA1422
        try
        {
            recorder.SetAudioSource(AudioSource.Mic);
            recorder.SetOutputFormat(OutputFormat.Mpeg4);
            recorder.SetAudioEncoder(AudioEncoder.Aac);
            recorder.SetAudioEncodingBitRate(128000);
            recorder.SetAudioSamplingRate(44100);
            recorder.SetOutputFile(filePath);
            recorder.Prepare();
            recorder.Start();
            _recorder = recorder;
            _recordingPath = filePath;
            _recordingClock.Restart();
        }
        catch
        {
            recorder.Release();
            recorder.Dispose();
            if (File.Exists(filePath)) File.Delete(filePath);
            throw;
        }
    }

    public long StopRecording()
    {
        var recorder = _recorder;
        if (recorder is null) return 0;
        _recorder = null;
        _recordingClock.Stop();
        var elapsed = _recordingClock.ElapsedMilliseconds;
        try
        {
            recorder.Stop();
        }
        finally
        {
            recorder.Reset();
            recorder.Release();
            recorder.Dispose();
        }
        if (_recordingPath is { Length: > 0 } path && File.Exists(path) && new FileInfo(path).Length == 0)
            File.Delete(path);
        _recordingPath = null;
        return elapsed;
    }

    public void CancelRecording()
    {
        var path = _recordingPath;
        try { StopRecording(); } catch { }
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
    }

    public void Play(string filePath, long startMilliseconds = 0)
    {
        if (IsRecording) throw new InvalidOperationException("Stop recording before playback.");
        StopPlayback();
        if (!File.Exists(filePath)) throw new FileNotFoundException("Audio file not found.", filePath);
        var player = new MediaPlayer();
        try
        {
            player.SetDataSource(filePath);
            player.Prepare();
            if (startMilliseconds > 0) player.SeekTo((int)Math.Clamp(startMilliseconds, 0, player.Duration));
            player.Start();
            _player = player;
        }
        catch
        {
            player.Release();
            player.Dispose();
            throw;
        }
    }

    public void PauseOrResume()
    {
        if (_player is null) return;
        if (_player.IsPlaying) _player.Pause();
        else _player.Start();
    }

    public void Seek(long milliseconds)
    {
        if (_player is null) return;
        _player.SeekTo((int)Math.Clamp(milliseconds, 0, _player.Duration));
    }

    public void StopPlayback()
    {
        var player = _player;
        _player = null;
        if (player is null) return;
        try { player.Stop(); } catch { }
        player.Reset();
        player.Release();
        player.Dispose();
    }

    public void Dispose()
    {
        try { StopRecording(); } catch { CancelRecording(); }
        StopPlayback();
    }
}
