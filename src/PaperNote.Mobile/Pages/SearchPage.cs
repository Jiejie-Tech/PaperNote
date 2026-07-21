using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using PaperNote.Core.Services;
using PaperNote.Mobile.Models;
using PaperNote.Mobile.Services;

namespace PaperNote.Mobile.Pages;

public sealed class SearchPage : ContentPage
{
    private readonly MobileNotebookRepository _repository;
    private readonly IServiceProvider _services;
    private readonly ObservableCollection<NotebookCard> _results = [];
    private readonly CollectionView _list;
    private CancellationTokenSource? _cts;

    public SearchPage(MobileNotebookRepository repository, IServiceProvider services)
    {
        _repository = repository;
        _services = services;
        Title = "全局搜索";
        BackgroundColor = UiTheme.Background;
        var search = new SearchBar { Placeholder = "搜索笔记本、页面标题和文本", Margin = 12, BackgroundColor = UiTheme.Surface };
        search.TextChanged += Search_TextChanged;
        _list = new CollectionView
        {
            ItemsSource = _results,
            SelectionMode = SelectionMode.Single,
            EmptyView = new Label { Text = "输入关键词开始搜索", TextColor = UiTheme.Muted, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center },
            ItemTemplate = new DataTemplate(() =>
            {
                var title = new Label { FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = UiTheme.Text };
                title.SetBinding(Label.TextProperty, nameof(NotebookCard.Title));
                var detail = new Label { FontSize = 13, TextColor = UiTheme.Muted };
                detail.SetBinding(Label.TextProperty, nameof(NotebookCard.Detail));
                return UiTheme.Card(new VerticalStackLayout { Spacing = 5, Children = { title, detail } });
            }),
            Margin = 14
        };
        _list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not NotebookCard card) return;
            _list.SelectedItem = null;
            await _repository.OpenAsync(card.Stored);
            await Navigation.PushAsync(_services.GetRequiredService<EditorPage>());
        };
        Content = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, Children = { search } };
        ((Grid)Content).Add(_list, 0, 1);
    }

    private async void Search_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var previous = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();
        var token = _cts.Token;
        try
        {
            await Task.Delay(250, token);
            _results.Clear();
            if (string.IsNullOrWhiteSpace(e.NewTextValue)) return;
            foreach (var item in await _repository.RefreshAsync(e.NewTextValue, cancellationToken: token))
                _results.Add(new NotebookCard { Stored = item });
        }
        catch (OperationCanceledException) { }
    }
}
