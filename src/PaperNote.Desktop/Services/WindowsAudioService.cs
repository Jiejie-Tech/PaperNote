using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;

namespace PaperNote.Desktop.Services;

public sealed record WindowsAudioRecordingResult(long DurationMilliseconds, long FileSize, string MimeType);

public sealed class WindowsAudioService : IDisposable
{
    private readonly Stopwatch _recordingStopwatch = new();
    private string? _recordingAlias;
    private string? _recordingPath;
    private MediaPlayer? _player;
    private bool _isPaused;

    public bool IsRecording => !string.IsNullOrWhiteSpace(_recordingAlias);
    public bool IsPlaying => _player is not null && !_isPaused;
    public bool IsPaused => _player is not null && _isPaused;
    public string? CurrentPlaybackPath { get; private set; }
    public long RecordingElapsedMilliseconds => IsRecording ? _recordingStopwatch.ElapsedMilliseconds : 0;
    public long PlaybackPositionMilliseconds => _player is null ? 0 : Math.Max(0, (long)_player.Position.TotalMilliseconds);
    public long PlaybackDurationMilliseconds => _player?.NaturalDuration.HasTimeSpan == true
        ? Math.Max(0, (long)_player.NaturalDuration.TimeSpan.TotalMilliseconds)
        : 0;

    public event EventHandler? PlaybackEnded;

    public void StartRecording(string destinationPath)
    {
        if (IsRecording) throw new InvalidOperationException("已有录音正在进行。");
        destinationPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        StopPlayback();

        var alias = $"papernote_{Guid.NewGuid():N}";
        try
        {
            SendMci($"open new type waveaudio alias {alias}");
            SendMci($"set {alias} time format milliseconds");
            SendMci($"record {alias}");
            _recordingAlias = alias;
            _recordingPath = destinationPath;
            _recordingStopwatch.Restart();
        }
        catch
        {
            TrySendMci($"close {alias}");
            TryDelete(destinationPath);
            throw;
        }
    }

    public WindowsAudioRecordingResult StopRecording()
    {
        if (!IsRecording || string.IsNullOrWhiteSpace(_recordingAlias) || string.IsNullOrWhiteSpace(_recordingPath))
            throw new InvalidOperationException("录音设备没有生成有效音频。");

        var alias = _recordingAlias;
        var path = _recordingPath;
        var elapsed = _recordingStopwatch.ElapsedMilliseconds;
        try
        {
            SendMci($"stop {alias}");
            var deviceLength = QueryMciLong($"status {alias} length");
            SendMci($"save {alias} \"{path}\"");
            var fileSize = File.Exists(path) ? new FileInfo(path).Length : 0;
            return new WindowsAudioRecordingResult(Math.Max(deviceLength, elapsed), fileSize, "audio/wav");
        }
        finally
        {
            TrySendMci($"close {alias}");
            _recordingStopwatch.Reset();
            _recordingAlias = null;
            _recordingPath = null;
        }
    }

    public void CancelRecording()
    {
        var path = _recordingPath;
        if (!string.IsNullOrWhiteSpace(_recordingAlias)) TrySendMci($"close {_recordingAlias}");
        _recordingStopwatch.Reset();
        _recordingAlias = null;
        _recordingPath = null;
        if (!string.IsNullOrWhiteSpace(path)) TryDelete(path);
    }

    public async Task PlayAsync(string filePath, long offsetMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        filePath = Path.GetFullPath(filePath);
        if (!File.Exists(filePath)) throw new FileNotFoundException("找不到录音文件。", filePath);
        if (IsRecording) throw new InvalidOperationException("请先停止录音再播放。");

        StopPlayback();
        var player = new MediaPlayer();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;
        EventHandler openedHandler = (_, _) => opened.TrySetResult();
        EventHandler<ExceptionEventArgs> failedHandler = (_, args) =>
        {
            failure = args.ErrorException;
            opened.TrySetResult();
        };
        player.MediaOpened += openedHandler;
        player.MediaFailed += failedHandler;
        player.MediaEnded += Player_MediaEnded;
        _player = player;
        CurrentPlaybackPath = filePath;
        _isPaused = false;

        try
        {
            player.Open(new Uri(filePath, UriKind.Absolute));
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
            if (failure is not null) throw new InvalidOperationException("无法打开录音文件。", failure);
            player.Position = TimeSpan.FromMilliseconds(Math.Clamp(offsetMilliseconds, 0, PlaybackDurationMilliseconds > 0 ? PlaybackDurationMilliseconds : long.MaxValue));
            player.Play();
        }
        catch
        {
            StopPlayback();
            throw;
        }
        finally
        {
            player.MediaOpened -= openedHandler;
            player.MediaFailed -= failedHandler;
        }
    }

    public void PausePlayback()
    {
        if (_player is null || _isPaused) return;
        _player.Pause();
        _isPaused = true;
    }

    public void ResumePlayback()
    {
        if (_player is null || !_isPaused) return;
        _player.Play();
        _isPaused = false;
    }

    public void Seek(long offsetMilliseconds)
    {
        if (_player is null) return;
        var maximum = PlaybackDurationMilliseconds > 0 ? PlaybackDurationMilliseconds : long.MaxValue;
        _player.Position = TimeSpan.FromMilliseconds(Math.Clamp(offsetMilliseconds, 0, maximum));
    }

    public void StopPlayback()
    {
        if (_player is null)
        {
            CurrentPlaybackPath = null;
            _isPaused = false;
            return;
        }

        var player = _player;
        _player = null;
        CurrentPlaybackPath = null;
        _isPaused = false;
        player.MediaEnded -= Player_MediaEnded;
        try { player.Stop(); } catch { }
        try { player.Close(); } catch { }
    }

    public void Dispose()
    {
        CancelRecording();
        StopPlayback();
        GC.SuppressFinalize(this);
    }

    private void Player_MediaEnded(object? sender, EventArgs e)
    {
        StopPlayback();
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private static long QueryMciLong(string command)
    {
        var buffer = new StringBuilder(64);
        var error = mciSendString(command, buffer, buffer.Capacity, IntPtr.Zero);
        if (error != 0) ThrowMci(error, command);
        return long.TryParse(buffer.ToString(), out var value) ? Math.Max(0, value) : 0;
    }

    private static void SendMci(string command)
    {
        var error = mciSendString(command, null, 0, IntPtr.Zero);
        if (error != 0) ThrowMci(error, command);
    }

    private static void TrySendMci(string command)
    {
        try { mciSendString(command, null, 0, IntPtr.Zero); } catch { }
    }

    private static void ThrowMci(uint error, string command)
    {
        var buffer = new StringBuilder(256);
        var message = mciGetErrorString(error, buffer, buffer.Capacity) ? buffer.ToString() : $"MCI 错误 {error}";
        throw new InvalidOperationException($"音频设备操作失败：{message}（{command}）");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool mciGetErrorString(uint errorCode, StringBuilder errorText, int errorTextSize);
}
