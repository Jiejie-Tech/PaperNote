using System.Collections.ObjectModel;
using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Mobile.Controls;
using PaperNote.Mobile.Models;
using PaperNote.Mobile.Services;

namespace PaperNote.Mobile.Pages;

public sealed class EditorPage : ContentPage
{
    private readonly MobileNotebookRepository _repository;
    private readonly MobileTransferService _transfer;
    private readonly AndroidPdfService _pdf;
    private readonly InkCanvasView _canvas = null!;
    private readonly Entry _title;
    private readonly Label _pageStatus;
    private readonly CollectionView _pages;
    private readonly ObservableCollection<PageCard> _pageCards = [];
    private readonly Grid _mainGrid;
    private readonly Button _undo;
    private readonly Button _redo;
    private readonly Button _widthButton;
    private readonly Button _colorButton;
    private readonly Button _fingerButton;
    private readonly Button _opacityButton;
    private readonly Button _eraserModeButton;
    private readonly Button _smoothingButton;
    private readonly Dictionary<InkCanvasTool, Button> _toolButtons = [];
    private readonly Dictionary<InkCanvasTool, string> _toolLabels = [];
    private CancellationTokenSource? _saveCts;
    private NotebookPage? _page;
    private bool _loading;
    private double _penWidth = 3.2;
    private double _highlighterWidth = 18;
    private string _color = "#1D2530";
    private readonly MobileAudioService _audio = new();
    private readonly IDispatcherTimer _audioTimer;
    private readonly Button _audioButton;
    private AudioRecording? _activeRecording;
    private AudioRecording? _playingRecording;
    private long _lastAutomaticAudioCue;
    private long _lastPresentedAudioCue = -1;
    private Window? _lifecycleWindow;

    public EditorPage(MobileNotebookRepository repository, MobileTransferService transfer, AndroidPdfService pdf)
    {
        _repository = repository;
        _transfer = transfer;
        _pdf = pdf;
        BackgroundColor = UiTheme.Background;
        Shell.SetNavBarIsVisible(this, false);

        var back = UiTheme.Button("‹ 资料库", Back_Clicked);
        back.AutomationId = "EditorBackButton";
        _title = new Entry { FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text, BackgroundColor = Colors.Transparent, MaxLength = 80, HorizontalOptions = LayoutOptions.Fill };
        _title.TextChanged += Title_TextChanged;
        var more = UiTheme.Button("更多", More_Clicked);
        more.AutomationId = "EditorMoreButton";
        var header = new Grid
        {
            Padding = new Thickness(10, 8),
            BackgroundColor = UiTheme.Surface,
            ZIndex = 20,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        header.Add(back); header.Add(_title, 1); header.Add(more, 2);

        _canvas = new InkCanvasView
        {
            BackgroundColor = Color.FromArgb("#E7EAF1"),
            FingerDrawingEnabled = Preferences.Default.Get("FingerDrawing", true),
            SmoothingEnabled = Preferences.Default.Get("InkSmoothing", true),
            InkOpacity = Preferences.Default.Get("InkOpacity", 1d)
        };
        _canvas.InkChanged += Canvas_InkChanged;
        _canvas.HistoryChanged += (_, _) => UpdateHistory();
        _canvas.SelectionChanged += (_, _) => UpdatePageStatus();
        _audioTimer = Dispatcher.CreateTimer();
        _audioTimer.Interval = TimeSpan.FromMilliseconds(250);
        _audioTimer.Tick += AudioTimer_Tick;

        var toolRow = CreateToolbarRow(5);
        AddTool(toolRow, InkCanvasTool.Pen, "钢笔", 0);
        AddTool(toolRow, InkCanvasTool.Highlighter, "荧光笔", 1);
        AddTool(toolRow, InkCanvasTool.Eraser, "橡皮擦", 2);
        AddTool(toolRow, InkCanvasTool.Pan, "平移", 3);
        AddTool(toolRow, InkCanvasTool.Select, "选择", 4);

        var settingRow = CreateToolbarRow(5);
        _widthButton = CreateToolbarButton("粗细 3.2", Width_Clicked, "InkWidthButton");
        _colorButton = CreateToolbarButton("颜色", Color_Clicked, "InkColorButton");
        _fingerButton = CreateToolbarButton("手指：开", ToggleFingerDrawing_Clicked, "FingerDrawingButton");
        _undo = CreateToolbarButton("撤销", (_, _) => { _canvas.Undo(); UpdateHistory(); }, "UndoButton");
        _redo = CreateToolbarButton("重做", (_, _) => { _canvas.Redo(); UpdateHistory(); }, "RedoButton");
        settingRow.Add(_widthButton, 0); settingRow.Add(_colorButton, 1); settingRow.Add(_fingerButton, 2); settingRow.Add(_undo, 3); settingRow.Add(_redo, 4);

        var advancedRow = CreateToolbarRow(3);
        _opacityButton = CreateToolbarButton("不透明 100%", Opacity_Clicked, "InkOpacityButton");
        _eraserModeButton = CreateToolbarButton("橡皮：局部", EraserMode_Clicked, "EraserModeButton");
        _smoothingButton = CreateToolbarButton("平滑：开", ToggleSmoothing_Clicked, "SmoothingButton");
        advancedRow.Add(_opacityButton, 0); advancedRow.Add(_eraserModeButton, 1); advancedRow.Add(_smoothingButton, 2);

        var toolbar = new VerticalStackLayout
        {
            Spacing = 5,
            Padding = new Thickness(8, 5, 8, 7),
            BackgroundColor = UiTheme.Surface,
            ZIndex = 20,
            Children = { toolRow, settingRow, advancedRow }
        };
        _pages = new CollectionView
        {
            ItemsSource = _pageCards,
            SelectionMode = SelectionMode.Single,
            BackgroundColor = UiTheme.Surface,
            ItemTemplate = new DataTemplate(() =>
            {
                var title = new Label { FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text };
                title.SetBinding(Label.TextProperty, nameof(PageCard.Title));
                var detail = new Label { FontSize = 11, TextColor = UiTheme.Muted };
                detail.SetBinding(Label.TextProperty, nameof(PageCard.Detail));
                return new Border
                {
                    Content = new VerticalStackLayout { Children = { title, detail } },
                    Padding = 12,
                    Margin = new Thickness(8, 5),
                    BackgroundColor = UiTheme.Background,
                    Stroke = UiTheme.Border,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }
                };
            })
        };
        _pages.SelectionChanged += Pages_SelectionChanged;
        var pagePanel = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, BackgroundColor = UiTheme.Surface };
        pagePanel.Add(new Label { Text = "页面", FontSize = 18, FontAttributes = FontAttributes.Bold, Margin = new Thickness(14, 12), TextColor = UiTheme.Text });
        pagePanel.Add(_pages, 0, 1);

        _mainGrid = new Grid
        {
            ColumnSpacing = 1,
            IsClippedToBounds = true,
            ZIndex = 0,
            ColumnDefinitions = { new ColumnDefinition(0), new ColumnDefinition(GridLength.Star) }
        };
        _mainGrid.Add(pagePanel); _mainGrid.Add(_canvas, 1);

        _pageStatus = new Label { TextColor = UiTheme.Muted, FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation, VerticalTextAlignment = TextAlignment.Center };
        var bottomButtons = new HorizontalStackLayout { Spacing = 7 };
        bottomButtons.Add(UiTheme.Button("页面", Pages_Clicked));
        bottomButtons.Add(UiTheme.Button("＋ 新页", AddPage_Clicked, primary: true));
        bottomButtons.Add(UiTheme.Button("PDF", Pdf_Clicked));
        _audioButton = UiTheme.Button("录音", Audio_Clicked);
        _audioButton.AutomationId = "AudioTimelineButton";
        bottomButtons.Add(_audioButton);
        var bottom = new Grid { Padding = new Thickness(10, 7, 10, 10), ZIndex = 20, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, BackgroundColor = UiTheme.Surface };
        bottom.Add(_pageStatus); bottom.Add(bottomButtons, 1);

        var root = new Grid
        {
            IsClippedToBounds = true,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }
        };
        root.Add(header); root.Add(toolbar, 0, 1); root.Add(_mainGrid, 0, 2); root.Add(bottom, 0, 3);
        Content = root;
        SizeChanged += (_, _) => _mainGrid.ColumnDefinitions[0].Width = Width >= 900 ? 230 : 0;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_repository.Current is null) { _ = Navigation.PopAsync(); return; }
        AttachLifecycleEvents();
        _canvas.FingerDrawingEnabled = Preferences.Default.Get("FingerDrawing", true);
        UpdateFingerDrawingButton();
        LoadNotebook();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        DetachLifecycleEvents();
        CancelPendingSave();
        StopActiveRecording();
        _audio.StopPlayback();
        _audioTimer.Stop();
        try { await _repository.SaveCurrentAsync(); } catch { }
    }

    private void AttachLifecycleEvents()
    {
        if (_lifecycleWindow is not null || Window is null) return;
        _lifecycleWindow = Window;
        _lifecycleWindow.Stopped += AppWindow_Stopped;
        _lifecycleWindow.Destroying += AppWindow_Destroying;
    }

    private void DetachLifecycleEvents()
    {
        if (_lifecycleWindow is null) return;
        _lifecycleWindow.Stopped -= AppWindow_Stopped;
        _lifecycleWindow.Destroying -= AppWindow_Destroying;
        _lifecycleWindow = null;
    }

    private async void AppWindow_Stopped(object? sender, EventArgs e)
    {
        await SaveForLifecycleChangeAsync();
    }

    private async void AppWindow_Destroying(object? sender, EventArgs e)
    {
        await SaveForLifecycleChangeAsync();
    }

    private async Task SaveForLifecycleChangeAsync()
    {
        CancelPendingSave();
        StopActiveRecording();
        _audio.StopPlayback();
        _audioTimer.Stop();
        try { await _repository.SaveCurrentAsync(); } catch { }
    }

    private void LoadNotebook()
    {
        var notebook = _repository.Current!.Document;
        _loading = true;
        _title.Text = notebook.Title;
        RefreshPageCards();
        LoadPage(_repository.GetCurrentPage());
        _loading = false;
        SelectTool(InkCanvasTool.Pen);
    }

    private void LoadPage(NotebookPage page)
    {
        if (_page is not null && _page.Id != page.Id)
        {
            StopActiveRecording();
            _audio.StopPlayback();
            _playingRecording = null;
        }
        _page = page;
        _repository.Current!.Document.CurrentPageId = page.Id;
        _canvas.Page = page;
        _canvas.Document = page.Ink;
        UpdatePageStatus();
        _canvas.ResetViewport();
        UpdateHistory();
    }

    private void RefreshPageCards()
    {
        var notebook = _repository.Current?.Document;
        if (notebook is null) return;
        _pageCards.Clear();
        for (var i = 0; i < notebook.Pages.Count; i++) _pageCards.Add(new PageCard { Page = notebook.Pages[i], Number = i + 1 });
    }

    private static Grid CreateToolbarRow(int columns)
    {
        var row = new Grid { ColumnSpacing = 5, HeightRequest = 42 };
        for (var i = 0; i < columns; i++) row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        return row;
    }

    private static Button CreateToolbarButton(string text, EventHandler clicked, string automationId)
    {
        var button = UiTheme.Button(text, clicked);
        button.AutomationId = automationId;
        button.FontSize = 12;
        button.HeightRequest = 42;
        button.MinimumHeightRequest = 42;
        button.MinimumWidthRequest = 0;
        button.Padding = new Thickness(3, 6);
        button.CornerRadius = 10;
        button.HorizontalOptions = LayoutOptions.Fill;
        return button;
    }

    private void AddTool(Grid toolbar, InkCanvasTool tool, string text, int column)
    {
        var button = CreateToolbarButton(text, (_, _) => SelectTool(tool), $"Tool{tool}Button");
        _toolButtons[tool] = button;
        _toolLabels[tool] = text;
        toolbar.Add(button, column);
    }

    private void SelectTool(InkCanvasTool tool)
    {
        _canvas.Tool = tool;
        _canvas.InkColor = _color;
        _canvas.InkWidth = tool == InkCanvasTool.Highlighter ? _highlighterWidth : _penWidth;
        foreach (var pair in _toolButtons)
        {
            var selected = pair.Key == tool;
            pair.Value.Text = selected ? $"{_toolLabels[pair.Key]} ✓" : _toolLabels[pair.Key];
            pair.Value.BackgroundColor = selected ? UiTheme.AccentSoft : UiTheme.Surface;
            pair.Value.TextColor = selected ? UiTheme.Accent : UiTheme.Text;
            pair.Value.BorderColor = selected ? UiTheme.Accent : UiTheme.Border;
        }
        UpdateToolSettingButtons();
    }

    private void UpdateToolSettingButtons()
    {
        var width = _canvas.Tool == InkCanvasTool.Highlighter ? _highlighterWidth : _penWidth;
        _widthButton.Text = $"粗细 {width:0.#}";
        _colorButton.Text = "颜色 ●";
        _colorButton.TextColor = Color.FromArgb(_color);
        _colorButton.BorderColor = Color.FromArgb(_color);
        UpdateFingerDrawingButton();
        UpdateAdvancedSettingsButtons();
    }

    private void UpdateAdvancedSettingsButtons()
    {
        _opacityButton.Text = $"不透明 {_canvas.InkOpacity * 100:0}%";
        _eraserModeButton.Text = _canvas.EraserMode == InkEraserMode.Partial ? "橡皮：局部" : "橡皮：整笔";
        _smoothingButton.Text = _canvas.SmoothingEnabled ? "平滑：开" : "平滑：关";
        _smoothingButton.BackgroundColor = _canvas.SmoothingEnabled ? UiTheme.AccentSoft : UiTheme.Surface;
        _smoothingButton.TextColor = _canvas.SmoothingEnabled ? UiTheme.Accent : UiTheme.Text;
    }

    private void UpdateFingerDrawingButton()
    {
        var enabled = _canvas.FingerDrawingEnabled;
        _fingerButton.Text = enabled ? "手指：开" : "手指：关";
        _fingerButton.BackgroundColor = enabled ? UiTheme.AccentSoft : UiTheme.Surface;
        _fingerButton.TextColor = enabled ? UiTheme.Accent : UiTheme.Text;
        _fingerButton.BorderColor = enabled ? UiTheme.Accent : UiTheme.Border;
        UpdatePageStatus();
    }

    private void UpdatePageStatus()
    {
        if (_page is null || _repository.Current is null) return;
        var index = _repository.Current.Document.Pages.IndexOf(_page);
        if (index < 0) return;
        var selected = _canvas.SelectedObjectCount > 0 ? $" · 已选 {_canvas.SelectedObjectCount} 个对象" : string.Empty;
        var audio = _activeRecording is not null ? " · 正在录音" : _page.AudioRecordings.Count > 0 ? $" · {_page.AudioRecordings.Count} 段录音" : string.Empty;
        _pageStatus.Text = $"{index + 1}/{_repository.Current.Document.Pages.Count} 页 · {(_canvas.FingerDrawingEnabled ? "手写开" : "手写关")}{selected}{audio}";
    }

    private async void Opacity_Clicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheetAsync("笔迹不透明度", "取消", null, "100%", "75%", "50%", "25%");
        var opacity = choice switch { "100%" => 1d, "75%" => .75d, "50%" => .5d, "25%" => .25d, _ => -1d };
        if (opacity <= 0) return;
        _canvas.InkOpacity = opacity;
        Preferences.Default.Set("InkOpacity", opacity);
        UpdateAdvancedSettingsButtons();
    }

    private async void EraserMode_Clicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheetAsync("橡皮模式", "取消", null, "局部橡皮", "整笔橡皮");
        if (choice == "局部橡皮") _canvas.EraserMode = InkEraserMode.Partial;
        else if (choice == "整笔橡皮") _canvas.EraserMode = InkEraserMode.Stroke;
        UpdateAdvancedSettingsButtons();
    }

    private void ToggleSmoothing_Clicked(object? sender, EventArgs e)
    {
        _canvas.SmoothingEnabled = !_canvas.SmoothingEnabled;
        Preferences.Default.Set("InkSmoothing", _canvas.SmoothingEnabled);
        UpdateAdvancedSettingsButtons();
    }

    private void ToggleFingerDrawing_Clicked(object? sender, EventArgs e)
    {
        _canvas.FingerDrawingEnabled = !_canvas.FingerDrawingEnabled;
        Preferences.Default.Set("FingerDrawing", _canvas.FingerDrawingEnabled);
        UpdateFingerDrawingButton();
    }
    private void Canvas_InkChanged(object? sender, EventArgs e)
    {
        if (_page is null) return;
        _page.ModifiedAt = DateTimeOffset.Now;
        if (_activeRecording is not null)
        {
            var elapsed = _audio.RecordingElapsedMilliseconds;
            if (elapsed - _lastAutomaticAudioCue >= 500)
            {
                _activeRecording.DurationMilliseconds = elapsed;
                var strokeId = _page.Ink.Strokes.LastOrDefault()?.Id;
                AudioTimelineService.AddCue(_activeRecording, elapsed, strokeId, "书写");
                _lastAutomaticAudioCue = elapsed;
            }
        }
        ScheduleSave();
        RefreshPageCards();
    }

    private void Title_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_loading || _repository.Current is null) return;
        _repository.Current.Document.Title = string.IsNullOrWhiteSpace(e.NewTextValue) ? "未命名笔记本" : e.NewTextValue.Trim();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        CancelPendingSave();
        _saveCts = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(_saveCts.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(700, token);
            await _repository.SaveCurrentAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelPendingSave()
    {
        var current = Interlocked.Exchange(ref _saveCts, null);
        if (current is null) return;
        current.Cancel();
        current.Dispose();
    }

    private async void Back_Clicked(object? sender, EventArgs e)
    {
        CancelPendingSave();
        await _repository.SaveCurrentAsync();
        await Navigation.PopAsync();
    }

    private async void More_Clicked(object? sender, EventArgs e)
    {
        if (_page is null || _repository.Current is null) return;
        var selected = GetSelectedObject();
        var selectedCount = _canvas.SelectedObjectCount;
        var selectedItems = _page.Objects.Where(item => _canvas.SelectedObjectIds.Contains(item.Id)).ToArray();
        var actions = new List<string>();
        if (selectedCount > 0)
        {
            if (selectedCount == 1 && selected?.Kind == "Text") actions.Add("\u7f16\u8f91\u9009\u4e2d\u6587\u5b57");
            if (selectedCount > 1) actions.Add("\u7ec4\u5408\u9009\u4e2d\u5bf9\u8c61");
            if (selectedItems.Any(item => item.GroupId is not null)) actions.Add("\u53d6\u6d88\u7ec4\u5408");
            actions.AddRange(["\u6539\u4e3a\u5f53\u524d\u989c\u8272", "\u8bbe\u7f6e\u900f\u660e\u5ea6", "\u590d\u5236\u9009\u4e2d\u5bf9\u8c61", "\u65cb\u8f6c 90\u00b0", "\u7f6e\u4e8e\u9876\u5c42", "\u7f6e\u4e8e\u5e95\u5c42"]);
            actions.Add(selectedItems.All(item => item.IsLocked) ? "\u89e3\u9501\u9009\u4e2d\u5bf9\u8c61" : "\u9501\u5b9a\u9009\u4e2d\u5bf9\u8c61");
            actions.Add("\u5220\u9664\u9009\u4e2d\u5bf9\u8c61");
        }
        actions.AddRange(["录音时间轴", "\u56fe\u5c42", "\u7eb8\u5f20\u8bbe\u7f6e", "\u9002\u5408\u5c4f\u5e55", "\u6e05\u7a7a\u5f53\u524d\u9875\u58a8\u8ff9", "\u91cd\u547d\u540d\u5f53\u524d\u9875", "\u6dfb\u52a0\u6587\u5b57", "\u6dfb\u52a0\u5f62\u72b6", "\u590d\u5236\u5f53\u524d\u9875", "\u5220\u9664\u5f53\u524d\u9875", "\u5bfc\u51fa\u7b14\u8bb0\u672c", "\u79fb\u5230\u56de\u6536\u7ad9"]);

        var choice = await DisplayActionSheetAsync("\u7b14\u8bb0\u672c\u64cd\u4f5c", "\u53d6\u6d88", null, actions.ToArray());
        switch (choice)
        {
            case "\u7f16\u8f91\u9009\u4e2d\u6587\u5b57":
                if (selected is not null)
                {
                    var text = await DisplayPromptAsync("\u7f16\u8f91\u6587\u5b57", "\u4fee\u6539\u6587\u5b57\u5185\u5bb9", initialValue: selected.Text, maxLength: 500, keyboard: Keyboard.Text);
                    if (text is not null) _canvas.UpdateSelectedText(text.Trim());
                }
                break;
            case "\u7ec4\u5408\u9009\u4e2d\u5bf9\u8c61": _canvas.GroupSelection(); break;
            case "\u53d6\u6d88\u7ec4\u5408": _canvas.UngroupSelection(); break;
            case "\u6539\u4e3a\u5f53\u524d\u989c\u8272": _canvas.UpdateSelectionStyle(_color, selected?.Opacity ?? 1); break;
            case "\u8bbe\u7f6e\u900f\u660e\u5ea6":
                var opacityChoice = await DisplayActionSheetAsync("\u5bf9\u8c61\u900f\u660e\u5ea6", "\u53d6\u6d88", null, "100%", "75%", "50%", "25%");
                var opacity = opacityChoice switch { "100%" => 1d, "75%" => .75d, "50%" => .5d, "25%" => .25d, _ => -1d };
                if (opacity > 0) _canvas.UpdateSelectionStyle(selected?.StrokeColor ?? _color, opacity);
                break;
            case "\u590d\u5236\u9009\u4e2d\u5bf9\u8c61": _canvas.DuplicateSelection(); break;
            case "\u65cb\u8f6c 90\u00b0": _canvas.RotateSelection(90); break;
            case "\u7f6e\u4e8e\u9876\u5c42": _canvas.BringSelectionToFront(); break;
            case "\u7f6e\u4e8e\u5e95\u5c42": _canvas.SendSelectionToBack(); break;
            case "\u9501\u5b9a\u9009\u4e2d\u5bf9\u8c61":
            case "\u89e3\u9501\u9009\u4e2d\u5bf9\u8c61": _canvas.ToggleSelectionLock(); break;
            case "\u5220\u9664\u9009\u4e2d\u5bf9\u8c61":
                if (await DisplayAlertAsync("\u5220\u9664\u5bf9\u8c61", "\u786e\u5b9a\u5220\u9664\u5f53\u524d\u9009\u4e2d\u7684\u5bf9\u8c61\u5417\uff1f", "\u5220\u9664", "\u53d6\u6d88")) _canvas.DeleteSelection();
                break;
            case "录音时间轴": await ShowAudioTimelineAsync(); break;
            case "\u56fe\u5c42": await ShowLayerMenuAsync(); break;
            case "\u7eb8\u5f20\u8bbe\u7f6e": Template_Clicked(sender, e); break;
            case "\u9002\u5408\u5c4f\u5e55": _canvas.ResetViewport(); break;
            case "\u6e05\u7a7a\u5f53\u524d\u9875\u58a8\u8ff9":
                if (!_page.Ink.IsEmpty && await DisplayAlertAsync("\u6e05\u7a7a\u58a8\u8ff9", "\u786e\u5b9a\u6e05\u7a7a\u5f53\u524d\u9875\u9762\u7684\u5168\u90e8\u624b\u5199\u7b14\u8ff9\u5417\uff1f\u9875\u9762\u5bf9\u8c61\u4e0d\u4f1a\u88ab\u5220\u9664\u3002", "\u6e05\u7a7a", "\u53d6\u6d88"))
                { _canvas.Clear(); ScheduleSave(); }
                break;
            case "\u91cd\u547d\u540d\u5f53\u524d\u9875":
                var title = await DisplayPromptAsync("\u9875\u9762\u6807\u9898", "\u8f93\u5165\u9875\u9762\u6807\u9898", initialValue: _page.Title, maxLength: 80);
                if (title is not null) { _page.Title = title.Trim(); RefreshPageCards(); ScheduleSave(); }
                break;
            case "\u6dfb\u52a0\u6587\u5b57":
                var addedText = await DisplayPromptAsync("\u6dfb\u52a0\u6587\u5b57", "\u8f93\u5165\u6587\u5b57\u5185\u5bb9", maxLength: 500, keyboard: Keyboard.Text);
                if (!string.IsNullOrWhiteSpace(addedText))
                {
                    var item = new PageObject { Kind = "Text", Text = addedText.Trim(), StrokeColor = _color, LayerId = PageLayerService.EnsureDefault(_page).Id };
                    _page.Objects.Add(item); _page.ModifiedAt = DateTimeOffset.Now;
                    _canvas.Page = null; _canvas.Page = _page; SelectTool(InkCanvasTool.Select); _canvas.SelectObject(item.Id); ScheduleSave();
                }
                break;
            case "\u6dfb\u52a0\u5f62\u72b6": await AddShapeAsync(); break;
            case "\u590d\u5236\u5f53\u524d\u9875": LoadPage(_repository.DuplicatePage(_page.Id)); RefreshPageCards(); ScheduleSave(); break;
            case "\u5220\u9664\u5f53\u524d\u9875":
                if (await DisplayAlertAsync("\u5220\u9664\u9875\u9762", "\u786e\u5b9a\u5220\u9664\u5f53\u524d\u9875\u9762\u5417\uff1f", "\u5220\u9664", "\u53d6\u6d88") && _repository.DeletePage(_page.Id))
                { RefreshPageCards(); LoadPage(_repository.GetCurrentPage()); ScheduleSave(); }
                break;
            case "\u5bfc\u51fa\u7b14\u8bb0\u672c": await _transfer.ShareNotebookAsync(); break;
            case "\u79fb\u5230\u56de\u6536\u7ad9":
                if (await DisplayAlertAsync("\u79fb\u5230\u56de\u6536\u7ad9", "\u7b14\u8bb0\u672c\u53ef\u901a\u8fc7\u684c\u9762\u7248\u6216\u5907\u4efd\u6062\u590d\u3002", "\u79fb\u9664", "\u53d6\u6d88"))
                { await _repository.MoveCurrentToTrashAsync(); await Navigation.PopAsync(); }
                break;
        }
    }

    private async void Audio_Clicked(object? sender, EventArgs e) => await ShowAudioTimelineAsync();

    private async Task ShowAudioTimelineAsync()
    {
        if (_page is null || _repository.Current is null) return;
        var actions = new List<string>();
        actions.Add(_activeRecording is null ? "开始录音" : "停止录音");
        if (_page.AudioRecordings.Count > 0)
        {
            actions.Add("播放录音");
            if (_audio.HasPlayback) actions.Add(_audio.IsPlaying ? "暂停播放" : "继续播放");
            if (_audio.HasPlayback) actions.Add("停止播放");
            if (_activeRecording is not null || _audio.HasPlayback) actions.Add("添加时间标记");
            actions.Add("跳转到时间标记");
            actions.Add("重命名录音");
            actions.Add("删除录音");
        }

        var choice = await DisplayActionSheetAsync("录音与笔迹时间轴", "取消", null, actions.ToArray());
        try
        {
            switch (choice)
            {
                case "开始录音": await StartAudioRecordingAsync(); break;
                case "停止录音": StopActiveRecording(); break;
                case "播放录音":
                    var recording = await ChooseRecordingAsync("选择要播放的录音");
                    if (recording is not null) PlayRecording(recording);
                    break;
                case "暂停播放":
                case "继续播放": _audio.PauseOrResume(); break;
                case "停止播放": StopAudioPlayback(); break;
                case "添加时间标记": await AddAudioCueAsync(); break;
                case "跳转到时间标记": await JumpToAudioCueAsync(); break;
                case "重命名录音": await RenameRecordingAsync(); break;
                case "删除录音": await DeleteRecordingAsync(); break;
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("录音操作失败", exception.Message, "知道了");
        }
        UpdateAudioButton();
        UpdatePageStatus();
    }

    private async Task StartAudioRecordingAsync()
    {
        if (_page is null || _repository.Current is null || _activeRecording is not null) return;
        if (!await _audio.EnsurePermissionAsync())
        {
            await DisplayAlertAsync("需要麦克风权限", "请允许 PaperNote 使用麦克风，录音只保存在本机。", "知道了");
            return;
        }

        StopAudioPlayback();
        var notebook = _repository.Current.Document;
        var recordingId = Guid.NewGuid();
        var path = AudioAttachmentService.PrepareRecordingPath(_repository.Storage.NotebooksDirectory, notebook.Id, _page.Id, recordingId, ".m4a");
        var storedPath = AudioAttachmentService.ToStoredPath(_repository.Storage.NotebooksDirectory, path);
        var recording = new AudioRecording
        {
            Id = recordingId,
            LocalFilePath = storedPath,
            DisplayName = $"录音 {_page.AudioRecordings.Count + 1}",
            MimeType = "audio/mp4"
        };
        _page.AudioRecordings.Add(recording);
        try
        {
            _audio.StartRecording(path);
            _activeRecording = recording;
            _lastAutomaticAudioCue = 0;
            _lastPresentedAudioCue = -1;
            _audioTimer.Start();
            ScheduleSave();
        }
        catch
        {
            _page.AudioRecordings.Remove(recording);
            AudioAttachmentService.TryDelete(_repository.Storage.NotebooksDirectory, storedPath);
            throw;
        }
    }

    private void StopActiveRecording()
    {
        var recording = _activeRecording;
        if (recording is null) return;
        _activeRecording = null;
        try
        {
            var duration = _audio.StopRecording();
            var path = AudioAttachmentService.ResolvePath(_repository.Storage.NotebooksDirectory, recording.LocalFilePath);
            var fileSize = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (duration < 300 || fileSize == 0)
            {
                _page?.AudioRecordings.Remove(recording);
                AudioAttachmentService.TryDelete(_repository.Storage.NotebooksDirectory, recording.LocalFilePath);
            }
            else
            {
                AudioTimelineService.UpdateRecording(recording, duration, fileSize);
                if (recording.Cues.Count == 0) AudioTimelineService.AddCue(recording, 0, label: "开始");
            }
        }
        catch
        {
            _audio.CancelRecording();
            _page?.AudioRecordings.Remove(recording);
            AudioAttachmentService.TryDelete(_repository.Storage.NotebooksDirectory, recording.LocalFilePath);
        }
        ScheduleSave();
        UpdateAudioButton();
        UpdatePageStatus();
    }

    private void PlayRecording(AudioRecording recording, long startMilliseconds = 0)
    {
        StopActiveRecording();
        var path = AudioAttachmentService.ResolvePath(_repository.Storage.NotebooksDirectory, recording.LocalFilePath);
        _audio.Play(path, startMilliseconds);
        _playingRecording = recording;
        _lastPresentedAudioCue = -1;
        _audioTimer.Start();
        UpdateAudioButton();
    }

    private void StopAudioPlayback()
    {
        _audio.StopPlayback();
        _playingRecording = null;
        _lastPresentedAudioCue = -1;
        if (_activeRecording is null) _audioTimer.Stop();
        UpdateAudioButton();
    }

    private async Task<AudioRecording?> ChooseRecordingAsync(string title)
    {
        if (_page is null || _page.AudioRecordings.Count == 0) return null;
        var labels = _page.AudioRecordings
            .Select((recording, index) => $"{index + 1}. {recording.DisplayName} · {AudioTimelineService.FormatDuration(recording.DurationMilliseconds)}")
            .ToArray();
        var choice = await DisplayActionSheetAsync(title, "取消", null, labels);
        var selectedIndex = Array.IndexOf(labels, choice);
        return selectedIndex >= 0 ? _page.AudioRecordings[selectedIndex] : null;
    }

    private async Task AddAudioCueAsync()
    {
        if (_page is null) return;
        var recording = _activeRecording ?? _playingRecording;
        if (recording is null) return;
        var offset = _activeRecording is not null ? _audio.RecordingElapsedMilliseconds : _audio.PlaybackPositionMilliseconds;
        if (_activeRecording is not null) recording.DurationMilliseconds = Math.Max(recording.DurationMilliseconds, offset);
        var label = await DisplayPromptAsync("添加时间标记", "标记名称", initialValue: "重点", maxLength: 40);
        if (label is null) return;
        var strokeId = _page.Ink.Strokes.LastOrDefault()?.Id;
        if (AudioTimelineService.AddCue(recording, offset, strokeId, label)) ScheduleSave();
    }

    private async Task JumpToAudioCueAsync()
    {
        var recording = _playingRecording ?? await ChooseRecordingAsync("选择录音");
        if (recording is null || recording.Cues.Count == 0)
        {
            await DisplayAlertAsync("没有时间标记", "请在录音或播放过程中添加标记。", "知道了");
            return;
        }
        var labels = recording.Cues.Select((cue, index) => $"{index + 1}. {AudioTimelineService.FormatDuration(cue.OffsetMilliseconds)} · {(string.IsNullOrWhiteSpace(cue.Label) ? "时间标记" : cue.Label)}").ToArray();
        var choice = await DisplayActionSheetAsync("跳转时间标记", "取消", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;
        PlayRecording(recording, recording.Cues[index].OffsetMilliseconds);
    }

    private async Task RenameRecordingAsync()
    {
        var recording = await ChooseRecordingAsync("选择录音");
        if (recording is null) return;
        var name = await DisplayPromptAsync("重命名录音", "录音名称", initialValue: recording.DisplayName, maxLength: 60);
        if (string.IsNullOrWhiteSpace(name)) return;
        recording.DisplayName = name.Trim();
        _page!.ModifiedAt = DateTimeOffset.Now;
        ScheduleSave();
    }

    private async Task DeleteRecordingAsync()
    {
        var recording = await ChooseRecordingAsync("选择要删除的录音");
        if (recording is null || _page is null) return;
        if (!await DisplayAlertAsync("删除录音", $"确定删除“{recording.DisplayName}”及其时间标记吗？", "删除", "取消")) return;
        if (_activeRecording?.Id == recording.Id) StopActiveRecording();
        if (_playingRecording?.Id == recording.Id) StopAudioPlayback();
        AudioAttachmentService.TryDelete(_repository.Storage.NotebooksDirectory, recording.LocalFilePath);
        AudioTimelineService.RemoveRecording(_page, recording.Id);
        ScheduleSave();
    }

    private void AudioTimer_Tick(object? sender, EventArgs e)
    {
        if (_activeRecording is not null)
        {
            _activeRecording.DurationMilliseconds = _audio.RecordingElapsedMilliseconds;
            UpdateAudioButton();
            return;
        }
        if (_playingRecording is null || !_audio.HasPlayback)
        {
            _audioTimer.Stop();
            UpdateAudioButton();
            return;
        }

        var position = _audio.PlaybackPositionMilliseconds;
        var duration = Math.Max(_playingRecording.DurationMilliseconds, _audio.PlaybackDurationMilliseconds);
        if (duration > 0 && position >= duration - 100 && !_audio.IsPlaying)
        {
            StopAudioPlayback();
            return;
        }
        var cue = _playingRecording.Cues.LastOrDefault(item => item.OffsetMilliseconds <= position && position - item.OffsetMilliseconds <= 600);
        if (cue is not null && cue.OffsetMilliseconds != _lastPresentedAudioCue)
            _lastPresentedAudioCue = cue.OffsetMilliseconds;
        UpdateAudioButton(cue?.Label);
    }

    private void UpdateAudioButton(string? cueLabel = null)
    {
        if (_audioButton is null) return;
        if (_activeRecording is not null)
        {
            _audioButton.Text = $"停止 {AudioTimelineService.FormatDuration(_audio.RecordingElapsedMilliseconds)}";
            _audioButton.BackgroundColor = Color.FromArgb("#FDE8E8");
            _audioButton.TextColor = Color.FromArgb("#B42318");
        }
        else if (_playingRecording is not null && _audio.HasPlayback)
        {
            var suffix = string.IsNullOrWhiteSpace(cueLabel) ? string.Empty : $" · {cueLabel}";
            _audioButton.Text = $"{(_audio.IsPlaying ? "播放" : "暂停")} {AudioTimelineService.FormatDuration(_audio.PlaybackPositionMilliseconds)}{suffix}";
            _audioButton.BackgroundColor = UiTheme.AccentSoft;
            _audioButton.TextColor = UiTheme.Accent;
        }
        else
        {
            _audioButton.Text = _page?.AudioRecordings.Count > 0 ? $"录音 {_page.AudioRecordings.Count}" : "录音";
            _audioButton.BackgroundColor = UiTheme.Surface;
            _audioButton.TextColor = UiTheme.Text;
        }
    }

    private async Task ShowLayerMenuAsync()
    {
        if (_page is null) return;
        var active = PageLayerService.EnsureDefault(_page);
        var action = await DisplayActionSheetAsync($"图层 · {active.Name}", "取消", null,
            "切换活动图层", "新建图层", "重命名当前图层", active.IsVisible ? "隐藏当前图层" : "显示当前图层",
            active.IsLocked ? "解锁当前图层" : "锁定当前图层", "设置图层透明度", "合并到其他图层", "删除当前图层");
        switch (action)
        {
            case "切换活动图层":
                var layerLabels = _page.Layers.Select((layer, i) => $"{i + 1}. {layer.Name}{(layer.Id == _page.ActiveLayerId ? " ✓" : string.Empty)}").ToArray();
                var layerChoice = await DisplayActionSheetAsync("选择活动图层", "取消", null, layerLabels);
                var layerIndex = Array.IndexOf(layerLabels, layerChoice);
                if (layerIndex >= 0) PageLayerService.SetActive(_page, _page.Layers[layerIndex].Id);
                break;
            case "新建图层":
                var newName = await DisplayPromptAsync("新建图层", "图层名称", initialValue: $"图层 {_page.Layers.Count + 1}", maxLength: 40);
                if (!string.IsNullOrWhiteSpace(newName)) PageLayerService.Add(_page, newName.Trim());
                break;
            case "重命名当前图层":
                var renamed = await DisplayPromptAsync("重命名图层", "图层名称", initialValue: active.Name, maxLength: 40);
                if (!string.IsNullOrWhiteSpace(renamed)) PageLayerService.Rename(_page, active.Id, renamed);
                break;
            case "隐藏当前图层":
            case "显示当前图层": PageLayerService.SetVisibility(_page, active.Id, !active.IsVisible); break;
            case "锁定当前图层":
            case "解锁当前图层": PageLayerService.SetLocked(_page, active.Id, !active.IsLocked); break;
            case "设置图层透明度":
                var opacityChoice = await DisplayActionSheetAsync("图层透明度", "取消", null, "100%", "75%", "50%", "25%");
                var opacity = opacityChoice switch { "100%" => 1d, "75%" => .75d, "50%" => .5d, "25%" => .25d, _ => -1d };
                if (opacity > 0) PageLayerService.SetOpacity(_page, active.Id, opacity);
                break;
            case "合并到其他图层":
            case "删除当前图层":
                var targets = _page.Layers.Where(layer => layer.Id != active.Id).ToArray();
                if (targets.Length == 0) { await DisplayAlertAsync("无法操作", "页面至少需要保留一个图层。", "知道了"); break; }
                var targetLabels = targets.Select(layer => layer.Name).ToArray();
                var targetChoice = await DisplayActionSheetAsync("选择目标图层", "取消", null, targetLabels);
                var target = targets.FirstOrDefault(layer => layer.Name == targetChoice);
                if (target is not null)
                {
                    if (action == "合并到其他图层") PageLayerService.MergeInto(_page, active.Id, target.Id);
                    else if (await DisplayAlertAsync("删除图层", "内容将移到目标图层。", "删除", "取消")) PageLayerService.Delete(_page, active.Id, target.Id);
                }
                break;
        }
        _canvas.Page = null; _canvas.Page = _page;
        UpdatePageStatus(); ScheduleSave();
    }

    private PageObject? GetSelectedObject()
    {
        if (_page is null || _canvas.SelectedObjectId is not Guid selectedId) return null;
        return _page.Objects.FirstOrDefault(item => item.Id == selectedId);
    }

    private async Task AddShapeAsync()
    {
        if (_page is null) return;

        var choice = await DisplayActionSheetAsync("添加形状", "取消", null, "矩形", "圆角矩形", "椭圆", "直线", "三角形", "菱形", "箭头");
        var shapeKind = choice switch
        {
            "矩形" => "Rectangle",
            "圆角矩形" => "RoundedRectangle",
            "椭圆" => "Ellipse",
            "直线" => "Line",
            "三角形" => "Triangle",
            "菱形" => "Diamond",
            "箭头" => "Arrow",
            _ => null
        };
        if (shapeKind is null) return;

        var shape = new PageObject
        {
            Kind = "Shape",
            ShapeKind = shapeKind,
            X = 220,
            Y = 260,
            Width = shapeKind == "Line" ? 360 : 300,
            Height = shapeKind == "Line" ? 80 : 180,
            StrokeColor = _color,
            FillColor = shapeKind is "Line" or "Arrow" ? "#00000000" : "#183978F6",
            LayerId = PageLayerService.EnsureDefault(_page).Id
        };
        _page.Objects.Add(shape);
        _page.ModifiedAt = DateTimeOffset.Now;
        _canvas.Page = null;
        _canvas.Page = _page;
        SelectTool(InkCanvasTool.Select);
        _canvas.SelectObject(shape.Id);
        ScheduleSave();
    }

    private async void Pages_Clicked(object? sender, EventArgs e)
    {
        if (_repository.Current is null) return;
        var labels = _repository.Current.Document.Pages.Select((p, i) => string.IsNullOrWhiteSpace(p.Title) ? $"第 {i + 1} 页" : $"第 {i + 1} 页 · {p.Title}").ToArray();
        var choice = await DisplayActionSheetAsync("跳转页面", "取消", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index >= 0) LoadPage(_repository.Current.Document.Pages[index]);
    }

    private void AddPage_Clicked(object? sender, EventArgs e)
    {
        var template = _page?.PaperTemplate ?? "Dotted";
        var color = _page?.PaperColor ?? "#FFFFFF";
        LoadPage(_repository.AddPage(template, color));
        RefreshPageCards();
        ScheduleSave();
    }

    private void Pages_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PageCard card) return;
        _pages.SelectedItem = null;
        LoadPage(card.Page);
    }

    private async void Color_Clicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheetAsync("墨迹颜色", "取消", null, "深灰", "蓝色", "红色", "绿色", "黄色");
        if (choice is null or "取消") return;
        _color = choice switch { "蓝色" => "#3157D5", "红色" => "#D94A4A", "绿色" => "#208B67", "黄色" => "#F0B429", _ => "#1D2530" };
        _canvas.InkColor = _color;
        UpdateToolSettingButtons();
    }

    private async void Width_Clicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheetAsync("笔迹粗细", "取消", null, "细", "中", "粗", "特粗");
        if (choice == "取消" || choice is null) return;
        _penWidth = choice switch { "细" => 2, "中" => 3.2, "粗" => 6, _ => 10 };
        _highlighterWidth = choice switch { "细" => 12, "中" => 18, "粗" => 26, _ => 36 };
        SelectTool(_canvas.Tool);
    }

    private async void Template_Clicked(object? sender, EventArgs e)
    {
        if (_page is null) return;
        var choice = await DisplayActionSheetAsync("纸张模板", "取消", null, "空白", "横线", "方格", "点阵");
        var template = choice switch { "空白" => "Blank", "横线" => "Lined", "方格" => "Grid", "点阵" => "Dotted", _ => null };
        if (template is null) return;
        _page.PaperTemplate = template;
        _canvas.Page = null; _canvas.Page = _page;
        ScheduleSave();
    }

    private async void Pdf_Clicked(object? sender, EventArgs e)
    {
        if (_repository.Current is null) return;
        var choice = await DisplayActionSheetAsync("PDF", "取消", null, "导入 PDF 页面", "导出全部页面为 PDF", "仅导出当前页");
        try
        {
            if (choice == "导入 PDF 页面")
            {
                var file = await _transfer.PickPdfAsync();
                if (file is null) return;
                var imported = await _pdf.ImportAsync(file);
                var notebook = _repository.Current.Document;
                var index = notebook.Pages.FindIndex(page => page.Id == notebook.CurrentPageId) + 1;
                notebook.Pages.InsertRange(index, imported);
                notebook.CurrentPageId = imported.First().Id;
                RefreshPageCards(); LoadPage(imported.First()); ScheduleSave();
            }
            else if (choice == "导出全部页面为 PDF") await _pdf.ExportAndShareAsync(_repository.Current.Document);
            else if (choice == "仅导出当前页" && _page is not null) await _pdf.ExportAndShareAsync(_repository.Current.Document, [_page]);
        }
        catch (Exception exception) { await DisplayAlertAsync("PDF 操作失败", exception.Message, "知道了"); }
    }

    private void UpdateHistory()
    {
        if (_undo is null || _redo is null) return;
        _undo.IsEnabled = _canvas.CanUndo;
        _redo.IsEnabled = _canvas.CanRedo;
    }
}
