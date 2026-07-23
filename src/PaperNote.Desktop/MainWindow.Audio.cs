using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Desktop.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private readonly WindowsAudioService _desktopAudioService = new();
    private readonly DispatcherTimer _audioStatusTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private AudioRecording? _desktopActiveRecording;
    private NotebookPage? _desktopRecordingPage;
    private AudioRecording? _desktopPlayingRecording;
    private Guid? _desktopHighlightedStrokeId;
    private bool _audioTimelineInitialized;

    private void EnsureAudioTimelineInitialized()
    {
        if (_audioTimelineInitialized) return;
        _audioTimelineInitialized = true;
        _audioStatusTimer.Tick += AudioStatusTimer_Tick;
        _desktopAudioService.PlaybackEnded += DesktopAudioService_PlaybackEnded;
        _audioStatusTimer.Start();
        UpdateAudioTimelineButton();
    }

    private void AudioActions_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage is null || _currentNotebook is null) return;
        EnsureAudioTimelineInitialized();
        var menu = BuildAudioTimelineMenu();
        menu.PlacementTarget = AudioTimelineButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu BuildAudioTimelineMenu()
    {
        var menu = new ContextMenu();
        if (_desktopAudioService.IsRecording)
        {
            menu.Items.Add(CreateMenuItem("停止并保存录音", string.Empty, (_, _) => StopDesktopRecording(showMessage: true), true));
            menu.Items.Add(CreateMenuItem("添加当前时间标记", string.Empty, (_, _) => AddDesktopAudioCue("手动标记"), true));
        }
        else
        {
            menu.Items.Add(CreateMenuItem("开始录音", string.Empty, (_, _) => StartDesktopRecording(), !_isReadOnly && _currentPage is not null));
        }

        if (_desktopAudioService.IsPlaying || _desktopAudioService.IsPaused)
        {
            menu.Items.Add(CreateMenuItem(_desktopAudioService.IsPaused ? "继续播放" : "暂停播放", string.Empty, (_, _) => ToggleDesktopPlaybackPause(), true));
            menu.Items.Add(CreateMenuItem("停止播放", string.Empty, (_, _) => StopDesktopPlayback(), true));
        }

        menu.Items.Add(new Separator());
        var recordings = _currentPage?.AudioRecordings?.OrderByDescending(item => item.CreatedAt).ToArray() ?? [];
        if (recordings.Length == 0)
        {
            menu.Items.Add(new MenuItem { Header = "本页暂无录音", IsEnabled = false });
            return menu;
        }

        foreach (var recording in recordings)
        {
            var recordingMenu = new MenuItem
            {
                Header = $"{recording.DisplayName}  {AudioTimelineService.FormatDuration(recording.DurationMilliseconds)}"
            };
            var path = ResolveAudioPath(recording);
            var exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            recordingMenu.Items.Add(CreateMenuItem("从头播放", string.Empty, async (_, _) => await PlayDesktopRecordingAsync(recording, 0), exists));
            recordingMenu.Items.Add(CreateMenuItem("在播放位置添加标记", string.Empty, (_, _) => AddCueAtPlaybackPosition(recording), exists && IsCurrentPlayback(recording)));

            var progress = IsCurrentPlayback(recording)
                ? AudioTimelineService.GetPlaybackProgress(recording, _desktopAudioService.PlaybackPositionMilliseconds)
                : -1;
            var waveformMenu = new MenuItem { Header = "波形与跳转", IsEnabled = exists };
            waveformMenu.Items.Add(new MenuItem
            {
                Header = AudioWaveformService.FormatCompact(recording.WaveformPeaks, 32, progress),
                IsEnabled = false
            });
            foreach (var (label, ratio) in new[] { ("从头播放", 0d), ("跳到 25%", .25d), ("跳到 50%", .5d), ("跳到 75%", .75d) })
            {
                var offset = (long)Math.Round(recording.DurationMilliseconds * ratio);
                waveformMenu.Items.Add(CreateMenuItem(label, string.Empty, async (_, _) => await PlayDesktopRecordingAsync(recording, offset), exists));
            }
            recordingMenu.Items.Add(waveformMenu);

            var cuesMenu = new MenuItem { Header = $"时间标记（{recording.Cues.Count}）", IsEnabled = recording.Cues.Count > 0 };
            foreach (var cue in recording.Cues.OrderBy(item => item.OffsetMilliseconds))
            {
                var cueCopy = cue;
                var label = string.IsNullOrWhiteSpace(cue.Label) ? "时间标记" : cue.Label;
                var cueMenu = new MenuItem { Header = $"{AudioTimelineService.FormatDuration(cue.OffsetMilliseconds)}  {label}" };
                cueMenu.Items.Add(CreateMenuItem("从这里播放", string.Empty, async (_, _) => await PlayDesktopRecordingAsync(recording, cueCopy.OffsetMilliseconds), exists));
                cueMenu.Items.Add(CreateMenuItem("移动时间", string.Empty, (_, _) => EditDesktopAudioCue(recording, cueCopy, "Move"), !_isReadOnly));
                cueMenu.Items.Add(CreateMenuItem("重命名", string.Empty, (_, _) => EditDesktopAudioCue(recording, cueCopy, "Rename"), !_isReadOnly));
                cueMenu.Items.Add(CreateMenuItem("删除标记", string.Empty, (_, _) => EditDesktopAudioCue(recording, cueCopy, "Delete"), !_isReadOnly));
                cuesMenu.Items.Add(cueMenu);
            }
            recordingMenu.Items.Add(cuesMenu);
            recordingMenu.Items.Add(CreateMenuItem("设置播放裁剪范围", string.Empty, (_, _) => SetDesktopAudioTrim(recording), !_isReadOnly));
            recordingMenu.Items.Add(CreateMenuItem("清除播放裁剪范围", string.Empty, (_, _) => ClearDesktopAudioTrim(recording), !_isReadOnly && (recording.TrimStartMilliseconds > 0 || recording.TrimEndMilliseconds > 0)));
            recordingMenu.Items.Add(new Separator());
            recordingMenu.Items.Add(CreateMenuItem("重命名", string.Empty, (_, _) => RenameDesktopRecording(recording), !_isReadOnly));
            recordingMenu.Items.Add(CreateMenuItem("删除", string.Empty, (_, _) => DeleteDesktopRecording(recording), !_isReadOnly));
            menu.Items.Add(recordingMenu);
        }

        return menu;
    }

    private void StartDesktopRecording()
    {
        if (_currentNotebook is null || _currentPage is null || _isReadOnly || _desktopAudioService.IsRecording) return;
        EnsureAudioTimelineInitialized();
        StopDesktopPlayback();
        var page = _currentPage;
        var recording = AudioTimelineService.AddRecording(page, string.Empty, 0, $"录音 {page.AudioRecordings.Count + 1}", mimeType: "audio/wav");
        var absolutePath = AudioAttachmentService.PrepareRecordingPath(
            _notebookStorage.NotebooksDirectory,
            _currentNotebook.Id,
            page.Id,
            recording.Id,
            ".wav");
        recording.LocalFilePath = AudioAttachmentService.ToStoredPath(_notebookStorage.NotebooksDirectory, absolutePath);
        recording.InputDeviceName = "系统默认录音设备";

        try
        {
            _desktopAudioService.StartRecording(absolutePath);
            _desktopActiveRecording = recording;
            _desktopRecordingPage = page;
            page.ModifiedAt = DateTimeOffset.Now;
            MarkDirty();
            StatusText.Text = "正在录音；书写时会自动加入时间标记。";
            UpdateAudioTimelineButton();
        }
        catch (Exception exception)
        {
            AudioTimelineService.RemoveRecording(page, recording.Id);
            AudioAttachmentService.TryDelete(_notebookStorage.NotebooksDirectory, recording.LocalFilePath);
            _desktopActiveRecording = null;
            _desktopRecordingPage = null;
            MessageBox.Show(this, $"无法开始录音。\n\n{exception.Message}", "录音失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopDesktopRecording(bool showMessage)
    {
        var recording = _desktopActiveRecording;
        var page = _desktopRecordingPage;
        if (recording is null || page is null || !_desktopAudioService.IsRecording) return;
        try
        {
            var result = _desktopAudioService.StopRecording();
            AudioTimelineService.UpdateRecording(recording, result.DurationMilliseconds, result.FileSize);
            recording.MimeType = result.MimeType;
            if (result.FileSize <= 44 || result.DurationMilliseconds < 250)
            {
                AudioTimelineService.RemoveRecording(page, recording.Id);
                AudioAttachmentService.TryDelete(_notebookStorage.NotebooksDirectory, recording.LocalFilePath);
            if (showMessage) StatusText.Text = "录音已取消，没有生成有效音频。";
            }
            else
            {
                var path = ResolveAudioPath(recording);
                try
                {
                    recording.WaveformPeaks = AudioWaveformService.ExtractWaveform(path).ToList();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    recording.WaveformPeaks = [];
                }
                page.ModifiedAt = DateTimeOffset.Now;
                MarkDirty();
                if (showMessage) StatusText.Text = $"录音已保存：{recording.DisplayName} · {AudioTimelineService.FormatDuration(recording.DurationMilliseconds)}";
            }
        }
        catch (Exception exception)
        {
            AudioTimelineService.RemoveRecording(page, recording.Id);
            AudioAttachmentService.TryDelete(_notebookStorage.NotebooksDirectory, recording.LocalFilePath);
            if (showMessage) MessageBox.Show(this, $"无法保存录音。\n\n{exception.Message}", "录音失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _desktopActiveRecording = null;
            _desktopRecordingPage = null;
            UpdateAudioTimelineButton();
        }
    }

    private async Task PlayDesktopRecordingAsync(AudioRecording recording, long offsetMilliseconds)
    {
        var path = ResolveAudioPath(recording);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(this, "录音附件不存在，可能已被移动或删除。", "找不到录音", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var start = recording.TrimStartMilliseconds > 0 ? recording.TrimStartMilliseconds : 0;
            var end = recording.TrimEndMilliseconds > start ? recording.TrimEndMilliseconds : recording.DurationMilliseconds;
            offsetMilliseconds = Math.Clamp(offsetMilliseconds <= 0 ? start : offsetMilliseconds, start, Math.Max(start, end));
            await _desktopAudioService.PlayAsync(path, offsetMilliseconds);
            _desktopPlayingRecording = recording;
            UpdateAudioPlaybackHighlight(AudioTimelineService.GetActiveStrokeId(recording, offsetMilliseconds));
            StatusText.Text = $"正在播放：{recording.DisplayName}";
            UpdateAudioTimelineButton();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法播放录音。\n\n{exception.Message}", "播放失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleDesktopPlaybackPause()
    {
        if (_desktopAudioService.IsPaused) _desktopAudioService.ResumePlayback();
        else _desktopAudioService.PausePlayback();
        UpdateAudioTimelineButton();
    }

    private void AddDesktopAudioCue(string label, Guid? strokeId = null)
    {
        if (_desktopActiveRecording is null || !_desktopAudioService.IsRecording) return;
        var offset = _desktopAudioService.RecordingElapsedMilliseconds;
        if (AudioTimelineService.AddCue(_desktopActiveRecording, offset, strokeId, label))
        {
            _desktopRecordingPage!.ModifiedAt = DateTimeOffset.Now;
            MarkDirty();
        StatusText.Text = $"已在 {AudioTimelineService.FormatDuration(offset)} 添加时间标记。";
        }
    }

    private void AddCueAtPlaybackPosition(AudioRecording recording)
    {
        if (!IsCurrentPlayback(recording)) return;
        var offset = _desktopAudioService.PlaybackPositionMilliseconds;
        if (AudioTimelineService.AddCue(recording, offset, label: "手动标记"))
        {
            _currentPage!.ModifiedAt = DateTimeOffset.Now;
            MarkDirty();
            StatusText.Text = $"已在 {AudioTimelineService.FormatDuration(offset)} 添加时间标记。";
        }
    }

    private void RenameDesktopRecording(AudioRecording recording)
    {
        var name = PromptForText("重命名录音", "录音名称", recording.DisplayName);
        if (string.IsNullOrWhiteSpace(name)) return;
        recording.DisplayName = name.Trim()[..Math.Min(name.Trim().Length, 80)];
        _currentPage!.ModifiedAt = DateTimeOffset.Now;
        MarkDirty();
        UpdateAudioTimelineButton();
    }

    private void DeleteDesktopRecording(AudioRecording recording)
    {
        if (_currentPage is null) return;
        var result = MessageBox.Show(this, $"确定删除“{recording.DisplayName}”吗？音频文件也会被删除。", "删除录音", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        if (IsCurrentPlayback(recording)) StopDesktopPlayback();
        AudioAttachmentService.TryDelete(_notebookStorage.NotebooksDirectory, recording.LocalFilePath);
        AudioTimelineService.RemoveRecording(_currentPage, recording.Id);
        MarkDirty();
        UpdateAudioTimelineButton();
    }

    private void RecordActiveAudioCueForInk(System.Windows.Ink.StrokeCollection addedStrokes)
    {
        if (_desktopActiveRecording is null || !_desktopAudioService.IsRecording || addedStrokes.Count == 0) return;
        var strokeId = WpfInkAdapter.ToPaperInk(addedStrokes).Strokes.FirstOrDefault()?.Id;
        AddDesktopAudioCue("书写", strokeId);
    }

    private void StopAudioForContextChange()
    {
        if (_desktopAudioService.IsRecording) StopDesktopRecording(showMessage: false);
        StopDesktopPlayback();
        UpdateAudioTimelineButton();
    }

    private void ShutdownDesktopAudio()
    {
        if (_desktopAudioService.IsRecording) StopDesktopRecording(showMessage: false);
        _audioStatusTimer.Stop();
        _audioStatusTimer.Tick -= AudioStatusTimer_Tick;
        _desktopAudioService.PlaybackEnded -= DesktopAudioService_PlaybackEnded;
        UpdateAudioPlaybackHighlight(null);
        _desktopAudioService.Dispose();
    }

    private string ResolveAudioPath(AudioRecording recording)
    {
        try { return AudioAttachmentService.ResolvePath(_notebookStorage.NotebooksDirectory, recording.LocalFilePath); }
        catch { return string.Empty; }
    }

    private bool IsCurrentPlayback(AudioRecording recording)
    {
        var path = ResolveAudioPath(recording);
        return !string.IsNullOrWhiteSpace(path) && string.Equals(path, _desktopAudioService.CurrentPlaybackPath, StringComparison.OrdinalIgnoreCase);
    }

    private void AudioStatusTimer_Tick(object? sender, EventArgs e)
    {
        if (_desktopPlayingRecording is not null && (_desktopAudioService.IsPlaying || _desktopAudioService.IsPaused))
        {
            if (_desktopPlayingRecording.TrimEndMilliseconds > _desktopPlayingRecording.TrimStartMilliseconds &&
                _desktopAudioService.PlaybackPositionMilliseconds >= _desktopPlayingRecording.TrimEndMilliseconds)
            {
                StopDesktopPlayback();
                StatusText.Text = "已播放到裁剪终点。";
                return;
            }
            var strokeId = AudioTimelineService.GetActiveStrokeId(
                _desktopPlayingRecording,
                _desktopAudioService.PlaybackPositionMilliseconds);
            UpdateAudioPlaybackHighlight(strokeId);
        }
        else
        {
            UpdateAudioPlaybackHighlight(null);
        }
        UpdateAudioTimelineButton();
    }

    private void StopDesktopPlayback()
    {
        _desktopAudioService.StopPlayback();
        _desktopPlayingRecording = null;
        UpdateAudioPlaybackHighlight(null);
        UpdateAudioTimelineButton();
    }

    private void UpdateAudioPlaybackHighlight(Guid? strokeId)
    {
        if (AudioPlaybackHighlight is null) return;
        if (strokeId is null || _currentPage is null)
        {
            _desktopHighlightedStrokeId = null;
            AudioPlaybackHighlight.Points.Clear();
            AudioPlaybackHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        var stroke = InkSurface.Strokes.FirstOrDefault(item => WpfInkAdapter.GetStrokeId(item) == strokeId.Value);
        if (stroke is null || !PageLayerService.IsContentVisible(_currentPage, WpfInkAdapter.GetLayerId(stroke)))
        {
            _desktopHighlightedStrokeId = null;
            AudioPlaybackHighlight.Points.Clear();
            AudioPlaybackHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        var points = new PointCollection(stroke.StylusPoints.Count + 1);
        foreach (var point in stroke.StylusPoints) points.Add(new Point(point.X, point.Y));
        if (points.Count == 1) points.Add(new Point(points[0].X + .1, points[0].Y + .1));
        _desktopHighlightedStrokeId = strokeId;
        AudioPlaybackHighlight.Points = points;
        AudioPlaybackHighlight.Visibility = Visibility.Visible;
    }

    private void DesktopAudioService_PlaybackEnded(object? sender, EventArgs e)
    {
        _desktopPlayingRecording = null;
        UpdateAudioPlaybackHighlight(null);
        StatusText.Text = "录音播放结束。";
        UpdateAudioTimelineButton();
    }

    private void UpdateAudioTimelineButton()
    {
        if (AudioTimelineButton is null) return;
        if (_desktopAudioService.IsRecording)
        {
            AudioTimelineButton.Content = $"录音 {AudioTimelineService.FormatDuration(_desktopAudioService.RecordingElapsedMilliseconds)}";
            AudioTimelineButton.Foreground = Brushes.Firebrick;
        }
        else if (_desktopAudioService.IsPlaying || _desktopAudioService.IsPaused)
        {
            var icon = _desktopAudioService.IsPaused ? "暂停" : "播放";
            AudioTimelineButton.Content = $"{icon} {AudioTimelineService.FormatDuration(_desktopAudioService.PlaybackPositionMilliseconds)}";
            AudioTimelineButton.Foreground = Brushes.DarkSlateBlue;
        }
        else
        {
            var count = _currentPage?.AudioRecordings?.Count ?? 0;
            AudioTimelineButton.Content = count > 0 ? $"录音 {count}" : "录音";
            AudioTimelineButton.ClearValue(ForegroundProperty);
        }
    }

    private string? PromptForText(string title, string label, string initialValue)
    {
        var input = new TextBox { Text = initialValue, Margin = new Thickness(0, 6, 0, 14), MinWidth = 320 };
        input.SelectAll();
        var ok = new Button { Content = "确定", IsDefault = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80 };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        var dialog = new Window
        {
            Owner = this,
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void EditDesktopAudioCue(AudioRecording recording, AudioCue cue, string action)
    {
        if (_isReadOnly) return;
        if (action == "Move")
        {
            var input = PromptForText("移动时间标记", "新的秒数", (cue.OffsetMilliseconds / 1000d).ToString("0.###"));
            if (double.TryParse(input, out var seconds) && seconds >= 0) AudioTimelineService.MoveCue(recording, cue.Id, (long)Math.Round(seconds * 1000));
        }
        else if (action == "Rename")
        {
            var name = PromptForText("重命名时间标记", "名称", cue.Label);
            if (name is not null) AudioTimelineService.RenameCue(recording, cue.Id, name);
        }
        else if (MessageBox.Show(this, "确定删除这个时间标记吗？录音文件不会被删除。", "删除时间标记", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            AudioTimelineService.RemoveCue(recording, cue.Id);
        }
        if (_currentPage is not null) _currentPage.ModifiedAt = DateTimeOffset.Now;
        MarkDirty();
        StatusText.Text = "录音时间标记已更新";
    }

    private void SetDesktopAudioTrim(AudioRecording recording)
    {
        if (_isReadOnly) return;
        var start = PromptForText("播放裁剪起点", "起始秒数", (recording.TrimStartMilliseconds / 1000d).ToString("0.###"));
        var currentEnd = recording.TrimEndMilliseconds > 0 ? recording.TrimEndMilliseconds : recording.DurationMilliseconds;
        var end = PromptForText("播放裁剪终点", "结束秒数", (currentEnd / 1000d).ToString("0.###"));
        if (!double.TryParse(start, out var startSeconds) || !double.TryParse(end, out var endSeconds)) return;
        AudioTimelineService.SetTrimRange(recording, (long)Math.Round(startSeconds * 1000), (long)Math.Round(endSeconds * 1000));
        if (_currentPage is not null) _currentPage.ModifiedAt = DateTimeOffset.Now;
        MarkDirty();
        StatusText.Text = $"播放范围：{AudioTimelineService.FormatDuration(recording.TrimStartMilliseconds)} - {AudioTimelineService.FormatDuration(recording.TrimEndMilliseconds)}";
    }

    private void ClearDesktopAudioTrim(AudioRecording recording)
    {
        if (_isReadOnly) return;
        AudioTimelineService.SetTrimRange(recording, 0, 0);
        if (_currentPage is not null) _currentPage.ModifiedAt = DateTimeOffset.Now;
        MarkDirty();
        StatusText.Text = "已清除录音裁剪范围";
    }
}
