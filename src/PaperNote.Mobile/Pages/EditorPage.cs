using System.Collections.ObjectModel;
using PaperNote.Core.Models;
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
    private readonly Dictionary<InkCanvasTool, Button> _toolButtons = [];
    private CancellationTokenSource? _saveCts;
    private NotebookPage? _page;
    private bool _loading;
    private double _penWidth = 3.2;
    private double _highlighterWidth = 18;
    private string _color = "#1D2530";

    public EditorPage(MobileNotebookRepository repository, MobileTransferService transfer, AndroidPdfService pdf)
    {
        _repository = repository;
        _transfer = transfer;
        _pdf = pdf;
        BackgroundColor = UiTheme.Background;
        Shell.SetNavBarIsVisible(this, false);

        var back = UiTheme.Button("‹ 资料库", Back_Clicked);
        _title = new Entry { FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text, BackgroundColor = Colors.Transparent, MaxLength = 80, HorizontalOptions = LayoutOptions.Fill };
        _title.TextChanged += Title_TextChanged;
        var more = UiTheme.Button("更多", More_Clicked);
        var header = new Grid { Padding = new Thickness(10, 8), ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(back); header.Add(_title, 1); header.Add(more, 2);

        var toolbar = new HorizontalStackLayout { Spacing = 7, Padding = new Thickness(10, 5) };
        AddTool(toolbar, InkCanvasTool.Pen, "钢笔");
        AddTool(toolbar, InkCanvasTool.Highlighter, "荧光笔");
        AddTool(toolbar, InkCanvasTool.Eraser, "橡皮擦");
        AddTool(toolbar, InkCanvasTool.Pan, "平移");
        _undo = UiTheme.Button("撤销", (_, _) => { _canvas.Undo(); UpdateHistory(); });
        _redo = UiTheme.Button("重做", (_, _) => { _canvas.Redo(); UpdateHistory(); });
        toolbar.Add(_undo); toolbar.Add(_redo);
        toolbar.Add(UiTheme.Button("颜色", Color_Clicked));
        toolbar.Add(UiTheme.Button("粗细", Width_Clicked));
        toolbar.Add(UiTheme.Button("纸张", Template_Clicked));
        toolbar.Add(UiTheme.Button("适合屏幕", (_, _) => _canvas.ResetViewport()));
        var toolbarScroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = toolbar, BackgroundColor = UiTheme.Surface };

        _canvas = new InkCanvasView
        {
            BackgroundColor = Color.FromArgb("#E7EAF1"),
            FingerDrawingEnabled = Preferences.Default.Get("FingerDrawing", false)
        };
        _canvas.InkChanged += Canvas_InkChanged;
        _canvas.HistoryChanged += (_, _) => UpdateHistory();

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

        _mainGrid = new Grid { ColumnSpacing = 1, ColumnDefinitions = { new ColumnDefinition(0), new ColumnDefinition(GridLength.Star) } };
        _mainGrid.Add(pagePanel); _mainGrid.Add(_canvas, 1);

        _pageStatus = new Label { TextColor = UiTheme.Muted, VerticalTextAlignment = TextAlignment.Center };
        var bottomButtons = new HorizontalStackLayout { Spacing = 7 };
        bottomButtons.Add(UiTheme.Button("页面", Pages_Clicked));
        bottomButtons.Add(UiTheme.Button("＋ 新页", AddPage_Clicked, primary: true));
        bottomButtons.Add(UiTheme.Button("PDF", Pdf_Clicked));
        var bottom = new Grid { Padding = new Thickness(10, 7, 10, 10), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, BackgroundColor = UiTheme.Surface };
        bottom.Add(_pageStatus); bottom.Add(bottomButtons, 1);

        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        root.Add(header); root.Add(toolbarScroll, 0, 1); root.Add(_mainGrid, 0, 2); root.Add(bottom, 0, 3);
        Content = root;
        SizeChanged += (_, _) => _mainGrid.ColumnDefinitions[0].Width = Width >= 900 ? 230 : 0;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_repository.Current is null) { _ = Navigation.PopAsync(); return; }
        LoadNotebook();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        CancelPendingSave();
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
        _page = page;
        _repository.Current!.Document.CurrentPageId = page.Id;
        _canvas.Page = page;
        _canvas.Document = page.Ink;
        var index = _repository.Current.Document.Pages.IndexOf(page);
        _pageStatus.Text = $"第 {index + 1} / {_repository.Current.Document.Pages.Count} 页 · 自动保存";
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

    private void AddTool(HorizontalStackLayout toolbar, InkCanvasTool tool, string text)
    {
        var button = UiTheme.Button(text, (_, _) => SelectTool(tool));
        _toolButtons[tool] = button;
        toolbar.Add(button);
    }

    private void SelectTool(InkCanvasTool tool)
    {
        _canvas.Tool = tool;
        _canvas.InkColor = _color;
        _canvas.InkWidth = tool == InkCanvasTool.Highlighter ? _highlighterWidth : _penWidth;
        foreach (var pair in _toolButtons)
        {
            pair.Value.BackgroundColor = pair.Key == tool ? UiTheme.AccentSoft : UiTheme.Surface;
            pair.Value.TextColor = pair.Key == tool ? UiTheme.Accent : UiTheme.Text;
        }
    }

    private void Canvas_InkChanged(object? sender, EventArgs e)
    {
        if (_page is null) return;
        _page.ModifiedAt = DateTimeOffset.Now;
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
        var choice = await DisplayActionSheetAsync("笔记本操作", "取消", null, "重命名当前页", "添加文字", "添加形状", "复制当前页", "删除当前页", "导出笔记本", "移到回收站");
        if (_page is null || _repository.Current is null) return;
        switch (choice)
        {
            case "重命名当前页":
                var title = await DisplayPromptAsync("页面标题", "输入页面标题", initialValue: _page.Title, maxLength: 80);
                if (title is not null) { _page.Title = title.Trim(); RefreshPageCards(); ScheduleSave(); }
                break;
            case "添加文字":
                var text = await DisplayPromptAsync("添加文字", "输入文字内容", maxLength: 500, keyboard: Keyboard.Text);
                if (!string.IsNullOrWhiteSpace(text)) { _page.Objects.Add(new PageObject { Kind = "Text", Text = text.Trim() }); ScheduleSave(); _canvas.Page = null; _canvas.Page = _page; }
                break;
            case "添加形状":
                await AddShapeAsync();
                break;
            case "复制当前页":
                LoadPage(_repository.DuplicatePage(_page.Id)); RefreshPageCards(); ScheduleSave(); break;
            case "删除当前页":
                if (await DisplayAlertAsync("删除页面", "确定删除当前页面吗？", "删除", "取消") && _repository.DeletePage(_page.Id))
                { RefreshPageCards(); LoadPage(_repository.GetCurrentPage()); ScheduleSave(); }
                break;
            case "导出笔记本": await _transfer.ShareNotebookAsync(); break;
            case "移到回收站":
                if (await DisplayAlertAsync("移到回收站", "笔记本可通过桌面版或备份恢复。", "移除", "取消"))
                { await _repository.MoveCurrentToTrashAsync(); await Navigation.PopAsync(); }
                break;
        }
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
            FillColor = shapeKind is "Line" or "Arrow" ? "#00000000" : "#183978F6"
        };
        _page.Objects.Add(shape);
        _page.ModifiedAt = DateTimeOffset.Now;
        _canvas.Page = null;
        _canvas.Page = _page;
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
        _color = choice switch { "蓝色" => "#3157D5", "红色" => "#D94A4A", "绿色" => "#208B67", "黄色" => "#F0B429", _ => "#1D2530" };
        _canvas.InkColor = _color;
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
