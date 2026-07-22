using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using PaperNote.Mobile.Models;
using PaperNote.Mobile.Services;

namespace PaperNote.Mobile.Pages;

public sealed class LibraryPage : ContentPage
{
    private readonly MobileNotebookRepository _repository;
    private readonly MobileTransferService _transfer;
    private readonly IServiceProvider _services;
    private readonly ObservableCollection<NotebookCard> _cards = [];
    private readonly CollectionView _collection;
    private readonly SearchBar _search;
    private readonly Label _summary;
    private CancellationTokenSource? _searchCts;

    public LibraryPage(MobileNotebookRepository repository, MobileTransferService transfer, IServiceProvider services)
    {
        _repository = repository;
        _transfer = transfer;
        _services = services;
        Title = "资料库";
        BackgroundColor = UiTheme.Background;

        var title = new Label { Text = "PaperNote", FontSize = 30, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text };
        _summary = new Label { Text = "正在读取资料库…", FontSize = 13, TextColor = UiTheme.Muted };
        var heading = new VerticalStackLayout { Spacing = 0, Children = { title, _summary } };
        var newButton = UiTheme.Button("＋ 新建", NewNotebook_Clicked, primary: true);
        var header = new Grid { Padding = new Thickness(20, 18, 20, 8), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(heading); header.Add(newButton, 1);

        _search = new SearchBar
        {
            Placeholder = "搜索标题、文件夹、页面和文字",
            Margin = new Thickness(16, 4),
            BackgroundColor = UiTheme.Surface,
            TextColor = UiTheme.Text,
            PlaceholderColor = UiTheme.Muted
        };
        _search.TextChanged += Search_TextChanged;

        _collection = new CollectionView
        {
            ItemsSource = _cards,
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(16, 8),
            EmptyView = new VerticalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = "还没有笔记本", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text, HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = "点“新建”开始书写，或导入已有 .papernote 文件。", FontSize = 14, TextColor = UiTheme.Muted, HorizontalTextAlignment = TextAlignment.Center }
                }
            },
            ItemTemplate = new DataTemplate(() =>
            {
                var accent = new BoxView { WidthRequest = 8, CornerRadius = 4, VerticalOptions = LayoutOptions.Fill };
                accent.SetBinding(BoxView.ColorProperty, nameof(NotebookCard.CoverColor));
                var name = new Label { FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text, LineBreakMode = LineBreakMode.TailTruncation };
                name.SetBinding(Label.TextProperty, nameof(NotebookCard.Title));
                var detail = new Label { FontSize = 13, TextColor = UiTheme.Muted };
                detail.SetBinding(Label.TextProperty, nameof(NotebookCard.Detail));
                var folder = new Label { FontSize = 12, TextColor = UiTheme.Accent, BackgroundColor = UiTheme.AccentSoft, Padding = new Thickness(8, 4) };
                folder.SetBinding(Label.TextProperty, nameof(NotebookCard.Folder));
                var info = new VerticalStackLayout { Spacing = 5, Children = { name, detail, folder } };
                var grid = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(8), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                grid.Add(accent); grid.Add(info, 1);
                grid.Add(new Label { Text = "›", FontSize = 30, TextColor = UiTheme.Muted, VerticalOptions = LayoutOptions.Center }, 2);
                return new Border
                {
                    Content = grid,
                    BackgroundColor = UiTheme.Surface,
                    Stroke = UiTheme.Border,
                    StrokeThickness = 1,
                    Padding = 15,
                    Margin = new Thickness(0, 0, 0, 10),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 }
                };
            })
        };
        _collection.SelectionChanged += Collection_SelectionChanged;

        var bottom = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(16, 8, 16, 16), HorizontalOptions = LayoutOptions.Center };
        bottom.Add(UiTheme.Button("导入", Import_Clicked));
        bottom.Add(UiTheme.Button("全局搜索", (_, _) => OpenPage<SearchPage>()));
        bottom.Add(UiTheme.Button("备份", (_, _) => OpenPage<BackupPage>()));
        bottom.Add(UiTheme.Button("设置", (_, _) => OpenPage<SettingsPage>()));
        var bottomScroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = bottom, HorizontalScrollBarVisibility = ScrollBarVisibility.Never };

        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        layout.Add(header); layout.Add(_search, 0, 1); layout.Add(_collection, 0, 2); layout.Add(bottomScroll, 0, 3);
        Content = layout;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync(_search.Text);
    }

    private async void NewNotebook_Clicked(object? sender, EventArgs e)
    {
        var title = await DisplayPromptAsync("新建笔记本", "输入笔记本名称", initialValue: $"新笔记 {DateTime.Now:MM-dd}", maxLength: 80);
        if (title is null) return;
        try
        {
            await _repository.CreateAsync(title);
            await OpenEditorAsync();
        }
        catch (Exception exception) { await ShowErrorAsync("新建失败", exception); }
    }

    private async void Import_Clicked(object? sender, EventArgs e)
    {
        try
        {
            var file = await _transfer.PickNotebookAsync();
            if (file is null) return;
            await _repository.ImportNotebookAsync(file);
            await OpenEditorAsync();
        }
        catch (Exception exception) { await ShowErrorAsync("导入失败", exception); }
    }

    private async void Recovery_Clicked(object? sender, EventArgs e)
    {
        try
        {
            await RefreshAsync(_search.Text);
            var candidates = _repository.RecoveryCandidates;
            if (candidates.Count == 0)
            {
                await DisplayAlertAsync("恢复中心", "没有发现待抢救的草稿或损坏笔记。", "知道了");
                return;
            }

            var labels = candidates.Select((candidate, index) =>
                $"{index + 1}. {(candidate.Kind == PaperNote.Core.Services.NotebookRecoveryKind.TemporaryDraft ? "草稿" : "损坏文件")} · {candidate.DisplayName}").ToArray();
            var choice = await DisplayActionSheetAsync("选择待抢救文件", "取消", null, labels);
            var selectedIndex = Array.IndexOf(labels, choice);
            if (selectedIndex < 0) return;

            var selected = candidates[selectedIndex];
            var recovery = await _repository.Storage.ReadForRecoveryAsync(selected.FilePath);
            var warning = string.IsNullOrWhiteSpace(recovery.Error) ? string.Empty : $"\n\n诊断：{recovery.Error}";
            if (!recovery.IsReadable || recovery.Document is null)
            {
                var preview = string.IsNullOrWhiteSpace(recovery.RawPreview)
                    ? "（没有可显示的原始文本）"
                    : recovery.RawPreview[..Math.Min(800, recovery.RawPreview.Length)];
                await DisplayAlertAsync("只读抢救", $"文件无法还原为可编辑笔记，但原文件会继续保留。{warning}\n\n{preview}", "知道了");
                return;
            }

            var save = await DisplayAlertAsync("只读抢救", $"已读取“{recovery.Document.Title}”，共 {recovery.Document.Pages.Count} 页。{warning}\n\n另存为新笔记本？原文件不会被覆盖或删除。", "另存副本", "取消");
            if (!save) return;
            var stored = await _repository.Storage.SaveRecoveryCopyAsync(selected.FilePath);
            await RefreshAsync(_search.Text);
            await DisplayAlertAsync("抢救完成", $"已创建“{stored.Document.Title}”。", "知道了");
        }
        catch (Exception exception) { await ShowErrorAsync("抢救失败", exception); }
    }

    private async void Collection_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not NotebookCard card) return;
        _collection.SelectedItem = null;
        try
        {
            await _repository.OpenAsync(card.Stored);
            await OpenEditorAsync();
        }
        catch (Exception exception) { await ShowErrorAsync("打开失败", exception); }
    }

    private async Task OpenEditorAsync() => await Navigation.PushAsync(_services.GetRequiredService<EditorPage>());

    private async void OpenPage<T>() where T : Page => await Navigation.PushAsync(_services.GetRequiredService<T>());

    private async void Search_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var previous = Interlocked.Exchange(ref _searchCts, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(250, token);
            await RefreshAsync(e.NewTextValue, token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshAsync(string? query, CancellationToken cancellationToken = default)
    {
        try
        {
            var notebooks = await _repository.RefreshAsync(query, cancellationToken: cancellationToken);
            _cards.Clear();
            foreach (var notebook in notebooks) _cards.Add(new NotebookCard { Stored = notebook });
            var recovered = _repository.LastRecoveryResults.Count(item => item.Recovered);
            var damaged = _repository.RecoveryCandidates.Count(item => item.Kind == PaperNote.Core.Services.NotebookRecoveryKind.CorruptedNotebook || !item.IsReadable);
            var recoverySuffix = recovered > 0 ? $" · 已恢复 {recovered} 份草稿" : damaged > 0 ? $" · {damaged} 个文件待抢救" : string.Empty;
            _summary.Text = (string.IsNullOrWhiteSpace(query) ? $"{_cards.Count} 本笔记 · 本地保存" : $"找到 {_cards.Count} 本笔记") + recoverySuffix;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { await ShowErrorAsync("读取失败", exception); }
    }

    private Task ShowErrorAsync(string title, Exception exception) => DisplayAlertAsync(title, exception.Message, "知道了");
}
