using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PaperNote.Core.Models;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;
using PaperNote.Desktop.ViewModels;

namespace PaperNote.Desktop;

public partial class MainWindow : Window
{
    private readonly InkStorageService _storage = new();
    private readonly NotebookStorageService _notebookStorage;
    private readonly InkHistoryService _history = new();
    private readonly DispatcherTimer _autosaveTimer;
    private readonly SemaphoreSlim _notebookSaveGate = new(1, 1);
    private readonly ObservableCollection<NotebookCardViewModel> _notebookCards = [];
    private readonly ObservableCollection<LibraryFilterViewModel> _libraryFilters = [];
    private readonly ObservableCollection<string> _folderChoices = [];
    private readonly ObservableCollection<PageItemViewModel> _pageItems = [];
    private IReadOnlyList<StoredNotebook> _storedNotebooks = [];
    private string _selectedLibraryFilter = "all";
    private bool _isRefreshingLibraryFilters;

    private StrokeCollection? _observedStrokes;
    private NotebookDocument? _currentNotebook;
    private NotebookPage? _currentPage;
    private string? _currentNotebookPath;
    private Color _currentColor = (Color)ColorConverter.ConvertFromString("#202124");
    private Color _penColor = (Color)ColorConverter.ConvertFromString("#202124");
    private Color _highlighterColor = (Color)ColorConverter.ConvertFromString("#F4C542");
    private string _activeTool = "Pen";
    private double _penThickness = 3.2;
    private double _highlighterThickness = 18;
    private double _eraserSize = 24;
    private bool _isInitialized;
    private bool _isRestoring;
    private bool _isLoadingNotebook;
    private bool _isSwitchingPage;
    private bool _isPointerActionActive;
    private bool _isSelectionActionActive;
    private bool _isReadOnly;
    private bool _isFullscreen;
    private bool _isDirty;
    private bool _isCloseRequested;
    private bool _allowClose;
    private bool _isClosed;
    private readonly bool _skipStartupInitialization;
    private long _revision;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private ResizeMode _previousResizeMode;

    public MainWindow()
        : this(new NotebookStorageService(), new WorkspaceStateService(), skipStartupInitialization: false)
    {
    }

    public MainWindow(NotebookStorageService notebookStorage, WorkspaceStateService workspaceStateService, bool skipStartupInitialization = true)
    {
        _notebookStorage = notebookStorage ?? throw new ArgumentNullException(nameof(notebookStorage));
        _workspaceStateService = workspaceStateService ?? throw new ArgumentNullException(nameof(workspaceStateService));
        _skipStartupInitialization = skipStartupInitialization;
        InitializeComponent();
        NotebookList.ItemsSource = _notebookCards;
        LibraryFilterList.ItemsSource = _libraryFilters;
        NotebookFolderCombo.ItemsSource = _folderChoices;
        PageListBox.ItemsSource = _pageItems;
        InitializePageNavigation();
        InitializeOutlineNavigation();
        InitializeWorkspaceNavigation();
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        _isInitialized = true;
        AttachStrokeEvents(InkSurface.Strokes);
        ApplyCurrentTool();
        ApplyZoom();
        UpdateHistoryButtons();
        UpdatePenSettingsButton();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_skipStartupInitialization)
        {
            ShowLibrary();
            return;
        }

        try
        {
            LibraryStatusText.Text = "正在读取本机笔记本…";
            var recoveryResults = await _notebookStorage.RecoverTemporaryDraftsAsync();
            var recoveryCandidates = await _notebookStorage.InspectRecoveryAsync();
            var recoveredCount = recoveryResults.Count(item => item.Recovered);
            var damagedCount = recoveryCandidates.Count(item => item.Kind == NotebookRecoveryKind.CorruptedNotebook || !item.IsReadable);
            var recoverySuffix = recoveredCount > 0 ? $" · 已恢复 {recoveredCount} 份草稿" : damagedCount > 0 ? $" · {damagedCount} 个文件待抢救" : string.Empty;
            await RefreshLibraryAsync();
            if (_isCloseRequested || _isClosed) return;
            if (_storedNotebooks.All(item => item.Document.IsInTrash))
            {
                var legacyStrokes = await _storage.LoadAsync();
                if (_isCloseRequested || _isClosed) return;
                var title = legacyStrokes.Count > 0 ? "迁移的快速笔记" : "我的笔记本";
                var inkData = legacyStrokes.Count > 0 ? PageThumbnailService.Serialize(legacyStrokes) : null;
                await _notebookStorage.CreateAsync(NotebookDocument.Create(title, inkData));
                if (_isCloseRequested || _isClosed) return;
                await RefreshLibraryAsync();
                LibraryStatusText.Text = (legacyStrokes.Count > 0 ? "已把旧版快速笔记迁移到新笔记本" : "已创建默认笔记本 · 内容只保存在本机") + recoverySuffix;
            }
            else
            {
                LibraryStatusText.Text = $"共 {_notebookCards.Count} 本 · 内容只保存在本机{recoverySuffix}";
            }
            if (_isCloseRequested || _isClosed) return;
            await RestoreWorkspaceStateAsync();
            if (_isCloseRequested || _isClosed) return;
            await InitializeSharedPaperTemplatesAsync();
            if (_isCloseRequested || _isClosed) return;
            ShowLibrary();
        }
        catch (Exception exception)
        {
            if (_isCloseRequested || _isClosed) return;
            LibraryStatusText.Text = $"书架读取失败：{exception.Message}";
            MessageBox.Show(this, $"无法打开笔记本书架。\n\n{exception.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshLibraryAsync()
    {
        _storedNotebooks = await _notebookStorage.ListAsync();
        RefreshFolderChoices();
        RefreshLibraryFilters();
        ApplyLibraryFilter();
    }

    private void RefreshFolderChoices()
    {
        var currentText = NotebookFolderCombo.Text;
        var folders = _storedNotebooks
            .Where(item => !item.Document.IsInTrash && !string.IsNullOrWhiteSpace(item.Document.FolderName))
            .Select(item => item.Document.FolderName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _folderChoices.Clear();
        foreach (var folder in folders) _folderChoices.Add(folder);
        if (!string.IsNullOrWhiteSpace(currentText)) NotebookFolderCombo.Text = currentText;
    }

    private void RefreshLibraryFilters()
    {
        _isRefreshingLibraryFilters = true;
        try
        {
            _libraryFilters.Clear();
            var active = _storedNotebooks.Where(item => !item.Document.IsInTrash).ToArray();
            _libraryFilters.Add(new LibraryFilterViewModel { Key = "all", Label = "全部笔记", Icon = "▦", Count = active.Length });
            _libraryFilters.Add(new LibraryFilterViewModel { Key = "recent", Label = "最近访问", Icon = "◷", Count = active.Count(item => item.Document.LastOpenedAt.HasValue) });
            _libraryFilters.Add(new LibraryFilterViewModel { Key = "unfiled", Label = "未分类", Icon = "◇", Count = active.Count(item => string.IsNullOrWhiteSpace(item.Document.FolderName)) });

            foreach (var group in active
                         .Where(item => !string.IsNullOrWhiteSpace(item.Document.FolderName))
                         .GroupBy(item => item.Document.FolderName, StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                _libraryFilters.Add(new LibraryFilterViewModel { Key = $"folder:{group.Key}", Label = group.Key, Icon = "▰", Count = group.Count() });
            }

            _libraryFilters.Add(new LibraryFilterViewModel { Key = "trash", Label = "回收站", Icon = "♲", Count = _storedNotebooks.Count(item => item.Document.IsInTrash) });
            var selected = _libraryFilters.FirstOrDefault(item => string.Equals(item.Key, _selectedLibraryFilter, StringComparison.OrdinalIgnoreCase)) ?? _libraryFilters[0];
            _selectedLibraryFilter = selected.Key;
            LibraryFilterList.SelectedItem = selected;
        }
        finally
        {
            _isRefreshingLibraryFilters = false;
        }
    }

    private void ApplyLibraryFilter()
    {
        var query = LibrarySearchBox?.Text?.Trim() ?? string.Empty;
        var filtered = FilterAndSortLibraryNotebooks(_storedNotebooks, _selectedLibraryFilter, query, _librarySort);

        _notebookCards.Clear();
        foreach (var result in filtered)
        {
            var stored = result.Stored;
            var document = stored.Document;
            ImageSource? cover = null;
            try
            {
                var firstPage = document.Pages.FirstOrDefault();
                cover = firstPage is null ? null : GetPageThumbnail(firstPage);
            }
            catch { }

            var openedText = document.LastOpenedAt.HasValue ? $"打开于 {document.LastOpenedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}" : string.Empty;
            _notebookCards.Add(new NotebookCardViewModel
            {
                FilePath = stored.FilePath,
                Title = document.Title,
                PageCountText = $"{document.Pages.Count} 页",
                ModifiedText = document.IsInTrash && document.TrashedAt is not null
                    ? $"移除于 {document.TrashedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}"
                    : (_librarySort == "Opened" || _selectedLibraryFilter == "recent") && openedText.Length > 0
                        ? openedText
                        : $"更新于 {document.ModifiedAt.LocalDateTime:yyyy-MM-dd HH:mm}",
                FolderText = string.IsNullOrWhiteSpace(document.FolderName) ? "未分类" : document.FolderName,
                CoverBrush = CreateCoverBrush(document.CoverStyle),
                CoverThumbnail = cover,
                IsInTrash = document.IsInTrash,
                MatchText = query.Length == 0 ? string.Empty : $"命中：{result.MatchSummary}"
            });
        }

        var selectedFilter = _libraryFilters.FirstOrDefault(item => string.Equals(item.Key, _selectedLibraryFilter, StringComparison.OrdinalIgnoreCase));
        var filterName = selectedFilter?.Label ?? "全部笔记";
        LibraryHeadingText.Text = query.Length == 0 ? filterName : $"{filterName} · 搜索“{query}”";
        EmptyLibraryText.Text = query.Length > 0 ? "没有找到匹配的笔记本" : _selectedLibraryFilter == "trash" ? "回收站是空的" : "这里还没有笔记本";
        EmptyLibraryText.Visibility = _notebookCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (LibraryView.IsVisible)
        {
            LibraryStatusText.Text = _notebookCards.Count == 0
                ? (query.Length > 0 ? "没有搜索到匹配内容" : _selectedLibraryFilter == "trash" ? "回收站中没有笔记本" : "这个分类中还没有笔记本")
                : $"{LibraryHeadingText.Text} · 共 {_notebookCards.Count} 本 · 内容只保存在本机";
        }
    }

    private static Brush CreateCoverBrush(string coverStyle)
    {
        var color = NotebookStorageService.NormalizeCoverStyle(coverStyle) switch
        {
            "Purple" => "#8067C8",
            "Green" => "#4F9B76",
            "Beige" => "#C4A77D",
            "Red" => "#C85E63",
            _ => "#4D7FCB"
        };
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private void LibraryFilterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingLibraryFilters || LibraryFilterList.SelectedItem is not LibraryFilterViewModel selected) return;
        _selectedLibraryFilter = selected.Key;
        ApplyLibraryFilter();
    }
    private async void NewNotebook_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isDirty) await SaveNotebookAsync();
            var newDocument = NotebookDocument.Create("新笔记本");
            if (_selectedLibraryFilter.StartsWith("folder:", StringComparison.OrdinalIgnoreCase)) newDocument.FolderName = _selectedLibraryFilter[7..];
            var stored = await _notebookStorage.CreateAsync(newDocument);
            await RefreshLibraryAsync();
            await OpenNotebookAsync(stored.FilePath);
            StatusText.Text = "新笔记本已创建，可以开始书写";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法新建笔记本。\n\n{exception.Message}", "新建失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OpenNotebookCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string filePath }) await OpenNotebookAsync(filePath);
    }

    private async Task OpenNotebookAsync(string filePath)
    {
        if (_isLoadingNotebook || _isCloseRequested || _isClosed) return;
        _isLoadingNotebook = true;
        try
        {
            if (_isDirty) await SaveNotebookAsync();
            SaveStateText.Text = "正在打开…";
            var document = await _notebookStorage.LoadAsync(filePath);
            if (document.IsInTrash) throw new InvalidOperationException("请先从回收站恢复这个笔记本。");
            ClearPageThumbnailCache();
            PageOverviewOverlay.Visibility = Visibility.Collapsed;
            OutlineOverlay.Visibility = Visibility.Collapsed;
            PdfCropEditorOverlay.Visibility = Visibility.Collapsed;
            PdfImportOptionsOverlay.Visibility = Visibility.Collapsed;
            PdfExportOptionsOverlay.Visibility = Visibility.Collapsed;
            PageOverviewSearchBox.Text = string.Empty;
            OutlineSearchBox.Text = string.Empty;
            _pendingPdfImportPath = string.Empty;
            _pendingPdfImportPageCount = 0;
            PageOverviewBookmarkedOnlyToggle.IsChecked = false;
            _currentNotebook = document;
            _currentNotebookPath = filePath;
            document.LastOpenedAt = DateTimeOffset.Now;
            NotebookTitleBox.Text = document.Title;
            NotebookFolderCombo.Text = document.FolderName;
            SelectCoverStyle(document.CoverStyle);
            ResetPageFilterControls();
            var selectedPage = document.Pages.FirstOrDefault(page => page.Id == document.CurrentPageId) ?? document.Pages[0];
            _currentPage = selectedPage;
            RefreshPageItems(selectedPage.Id);
            LoadPage(selectedPage);
            ResetPageVisitHistory();
            RegisterOpenNotebookTab(filePath, document.Title);
            _revision = 0;
            _isDirty = false;
            SaveStateText.Text = "已保存";
            ShowEditor();
            MarkDirty();
            StatusText.Text = $"第 {document.Pages.IndexOf(selectedPage) + 1} 页 · {_activeToolDisplayName()} · {InkSurface.Strokes.Count} 条笔迹";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法打开笔记本。\n\n{exception.Message}", "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _isLoadingNotebook = false; }
    }

    private void ShowLibrary()
    {
        LibraryView.Visibility = Visibility.Visible;
        EditorView.Visibility = Visibility.Collapsed;
        Title = "PaperNote · 我的笔记本";
    }

    private void ShowEditor()
    {
        LibraryView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Visible;
        Title = $"PaperNote · {_currentNotebook?.Title ?? "笔记本"}";
        ApplyCurrentTool();
    }

    private async void BackToLibrary_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isDirty) await SaveNotebookAsync();
            await RefreshLibraryAsync();
            ShowLibrary();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"返回书架前保存失败。\n\n{exception.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteNotebook_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string filePath }) return;
        var card = _notebookCards.FirstOrDefault(item => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        var result = MessageBox.Show(this, $"将“{card?.Title ?? "这个笔记本"}”移到回收站吗？\n\n之后可以从回收站恢复。", "移到回收站", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            await _notebookStorage.MoveToTrashAsync(filePath);
            if (string.Equals(_currentNotebookPath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                _currentNotebook = null; _currentPage = null; _currentNotebookPath = null; _isDirty = false;
            }
            RemoveNotebookTab(filePath);
            await RefreshLibraryAsync();
            LibraryStatusText.Text = "已移到回收站，可随时恢复";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法把笔记本移到回收站。\n\n{exception.Message}", "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RestoreNotebook_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string filePath }) return;
        try
        {
            await _notebookStorage.RestoreAsync(filePath);
            await RefreshLibraryAsync();
            LibraryStatusText.Text = "笔记本已恢复到书架";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法恢复笔记本。\n\n{exception.Message}", "恢复失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PermanentlyDeleteNotebook_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string filePath }) return;
        var card = _notebookCards.FirstOrDefault(item => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        var result = MessageBox.Show(this, $"永久删除“{card?.Title ?? "这个笔记本"}”吗？\n\n文件和全部页面将无法恢复。", "永久删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            _notebookStorage.PermanentlyDelete(filePath);
            await RefreshLibraryAsync();
            LibraryStatusText.Text = "笔记本已永久删除";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法永久删除笔记本。\n\n{exception.Message}", "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void NotebookFolderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || _currentNotebook is null) return;
        if (NotebookFolderCombo.SelectedItem is string selected) ApplyFolderName(selected);
    }

    private void NotebookFolderCombo_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || _currentNotebook is null) return;
        ApplyFolderName(NotebookFolderCombo.Text);
    }

    private void NotebookFolderCombo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyFolderName(NotebookFolderCombo.Text);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void ApplyFolderName(string? folderName)
    {
        if (_currentNotebook is null || _isLoadingNotebook) return;
        var normalized = NotebookStorageService.NormalizeFolderName(folderName);
        NotebookFolderCombo.Text = normalized;
        if (string.Equals(_currentNotebook.FolderName, normalized, StringComparison.Ordinal)) return;
        _currentNotebook.FolderName = normalized;
        if (!string.IsNullOrWhiteSpace(normalized) && !_folderChoices.Contains(normalized)) _folderChoices.Add(normalized);
        MarkDirty();
        StatusText.Text = string.IsNullOrWhiteSpace(normalized) ? "已设为未分类" : $"已归入文件夹：{normalized}";
    }

    private void CoverStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || _currentNotebook is null || CoverStyleCombo.SelectedItem is not ComboBoxItem item) return;
        var coverStyle = NotebookStorageService.NormalizeCoverStyle(item.Tag?.ToString());
        if (string.Equals(_currentNotebook.CoverStyle, coverStyle, StringComparison.Ordinal)) return;
        _currentNotebook.CoverStyle = coverStyle;
        MarkDirty();
        StatusText.Text = $"封面已更换为{item.Content}";
    }

    private void SelectCoverStyle(string coverStyle)
    {
        var normalized = NotebookStorageService.NormalizeCoverStyle(coverStyle);
        foreach (var item in CoverStyleCombo.Items.OfType<ComboBoxItem>())
        {
            if (!string.Equals(item.Tag?.ToString(), normalized, StringComparison.Ordinal)) continue;
            CoverStyleCombo.SelectedItem = item;
            return;
        }
        CoverStyleCombo.SelectedIndex = 0;
    }
    private void NotebookTitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || _currentNotebook is null) return;
        var title = NotebookTitleBox.Text.Trim();
        _currentNotebook.Title = string.IsNullOrWhiteSpace(title) ? "未命名笔记本" : title;
        Title = $"PaperNote · {_currentNotebook.Title}";
        UpdateCurrentNotebookTabTitle();
        MarkDirty();
    }

    private void CaptureCurrentPage()
    {
        if (_currentNotebook is null || _currentPage is null) return;
        var inkData = PageThumbnailService.Serialize(InkSurface.Strokes);
        var portableInk = WpfInkAdapter.ToPaperInk(InkSurface.Strokes);
        var portableInkChanged = !string.Equals(
            PaperNote.Core.Ink.PaperInkSerializer.Serialize(_currentPage.Ink),
            PaperNote.Core.Ink.PaperInkSerializer.Serialize(portableInk),
            StringComparison.Ordinal);
        if (!string.Equals(_currentPage.InkData, inkData, StringComparison.Ordinal) || portableInkChanged)
        {
            _currentPage.InkData = inkData;
            _currentPage.Ink = portableInk;
            _currentPage.ModifiedAt = DateTimeOffset.Now;
            InvalidatePageThumbnail(_currentPage.Id);
        }
        _currentNotebook.CurrentPageId = _currentPage.Id;
        var item = _pageItems.FirstOrDefault(page => page.Id == _currentPage.Id);
        if (item is not null) item.Thumbnail = GetPageThumbnail(_currentPage);
        var overviewItem = _overviewPageItems.FirstOrDefault(page => page.Id == _currentPage.Id);
        if (overviewItem is not null) overviewItem.Thumbnail = GetPageThumbnail(_currentPage, 180, 255);
    }

    private void LoadPage(NotebookPage page)
    {
        if (_currentPage is not null && !ReferenceEquals(_currentPage, page)) StopAudioForContextChange();
        _isSwitchingPage = true;
        try
        {
            _currentPage = page;
            UpdateCurrentPageMetadataControls(page);
            ApplyPageAppearance(page);
            ReplaceStrokes(WpfInkAdapter.GetPageStrokes(page, migrateLegacyInk: true), false);
            LoadPageObjects(page);
            _history.Clear();
            UpdateHistoryButtons();
            UpdatePageNavigationStatus();
        }
        finally { _isSwitchingPage = false; }
    }

    private void RefreshPageItems(Guid? selectedId = null, IReadOnlySet<Guid>? selectedIds = null)
    {
        if (_currentNotebook is null) { _pageItems.Clear(); return; }
        _isSwitchingPage = true;
        try
        {
            _pageItems.Clear();
            for (var index = 0; index < _currentNotebook.Pages.Count; index++)
            {
                var page = _currentNotebook.Pages[index];
                if (!MatchesPageFilter(page, index)) continue;
                _pageItems.Add(new PageItemViewModel
                {
                    Id = page.Id,
                    Number = index + 1,
                    Title = page.Title,
                    IsBookmarked = page.IsBookmarked,
                    Thumbnail = GetPageThumbnail(page)
                });
            }

            var id = selectedId ?? _currentNotebook.CurrentPageId;
            var currentItem = _pageItems.FirstOrDefault(item => item.Id == id);
            if (currentItem is null && !IsPageFilterActive()) currentItem = _pageItems.FirstOrDefault();
            PageListBox.SelectedItems.Clear();
            var idsToRestore = selectedIds is { Count: > 0 }
                ? selectedIds
                : currentItem is not null ? new HashSet<Guid> { currentItem.Id } : new HashSet<Guid>();
            foreach (var item in _pageItems.Where(item => idsToRestore.Contains(item.Id))) PageListBox.SelectedItems.Add(item);
            if (currentItem is not null && !PageListBox.SelectedItems.Contains(currentItem)) PageListBox.SelectedItems.Add(currentItem);
            if (currentItem is not null) PageListBox.ScrollIntoView(currentItem);
            UpdatePageNavigationStatus();
            if (PageOverviewOverlay.Visibility == Visibility.Visible) RefreshPageOverviewItems(selectedId, selectedIds);
            RefreshOutlineIfVisible();
        }
        finally { _isSwitchingPage = false; }
    }

    private void PageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingNotebook || _isSwitchingPage || _currentNotebook is null) return;
        var selected = e.AddedItems.OfType<PageItemViewModel>().LastOrDefault() ?? PageListBox.SelectedItem as PageItemViewModel;
        if (selected is null) return;
        if (_currentPage?.Id != selected.Id)
        {
            CaptureCurrentPage();
            if (_currentPage is not null) RecordPageVisit(_currentPage.Id, selected.Id);
            var page = _currentNotebook.Pages.First(item => item.Id == selected.Id);
            _currentNotebook.CurrentPageId = page.Id;
            LoadPage(page);
            MarkDirty();
        }
        var selectionCount = PageListBox.SelectedItems.Count;
        StatusText.Text = selectionCount > 1
            ? $"已选择 {selectionCount} 页 · 当前为{selected.NumberText}"
            : $"{selected.NumberText} · {_activeToolDisplayName()} · {InkSurface.Strokes.Count} 条笔迹";
    }

    private void AddPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null || sender is not Button button) return;
        ShowAddPageMenu(button);
    }

    private void DuplicatePage_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { CommandParameter: PageItemViewModel item }) DuplicatePage(item.Id);
    }

    private void DuplicatePage(Guid pageId)
    {
        if (_currentNotebook is null) return;
        CaptureCurrentPage();
        var sourceIndex = _currentNotebook.Pages.FindIndex(page => page.Id == pageId);
        if (sourceIndex < 0) return;

        var source = _currentNotebook.Pages[sourceIndex];
        var copy = CloneNotebookPage(source);
        copy.CreatedAt = DateTimeOffset.Now;
        copy.ModifiedAt = copy.CreatedAt;
        var insertIndex = sourceIndex + 1;
        _currentNotebook.Pages.Insert(insertIndex, copy);
        _currentNotebook.CurrentPageId = copy.Id;
        _currentPage = copy;
        RefreshPageItems(copy.Id);
        LoadPage(copy);
        MarkDirty();
        StatusText.Text = $"已复制为第 {insertIndex + 1} 页";
    }

    private void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_currentNotebook is null || sender is not Button { CommandParameter: PageItemViewModel item }) return;
        if (_currentNotebook.Pages.Count <= 1)
        {
            MessageBox.Show(this, "每个笔记本至少需要保留一页。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var result = MessageBox.Show(this, $"确定删除第 {item.Number} 页吗？此操作无法撤销。", "删除页面", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        CaptureCurrentPage();
        var index = _currentNotebook.Pages.FindIndex(page => page.Id == item.Id);
        if (index < 0) return;
        var deletingCurrent = _currentPage?.Id == item.Id;
        _currentNotebook.Pages.RemoveAt(index);
        RemoveBrokenPageLinks();
        InvalidatePageThumbnail(item.Id);
        if (deletingCurrent)
        {
            var nextIndex = Math.Min(index, _currentNotebook.Pages.Count - 1);
            _currentPage = _currentNotebook.Pages[nextIndex];
            _currentNotebook.CurrentPageId = _currentPage.Id;
            LoadPage(_currentPage);
        }
        RefreshPageItems(_currentPage?.Id);
        MarkDirty();
        StatusText.Text = $"已删除页面 · 共 {_currentNotebook.Pages.Count} 页";
    }

    private void MovePageUp_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { CommandParameter: PageItemViewModel item }) MovePage(item.Id, -1);
    }

    private void MovePageDown_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { CommandParameter: PageItemViewModel item }) MovePage(item.Id, 1);
    }

    private void MovePage(Guid pageId, int direction)
    {
        MovePages(new HashSet<Guid> { pageId }, direction);
    }

    private void MoveSelectedPagesUp_Click(object sender, RoutedEventArgs e) => MovePages(GetSelectedPageIds(), -1);
    private void MoveSelectedPagesDown_Click(object sender, RoutedEventArgs e) => MovePages(GetSelectedPageIds(), 1);

    private HashSet<Guid> GetSelectedPageIds()
    {
        var overviewActive = PageOverviewOverlay.Visibility == Visibility.Visible;
        var ids = overviewActive
            ? PageOverviewListBox.SelectedItems.OfType<PageItemViewModel>().Select(item => item.Id).ToHashSet()
            : PageListBox.SelectedItems.OfType<PageItemViewModel>().Select(item => item.Id).ToHashSet();
        if (ids.Count == 0 && !overviewActive && _currentNotebook is not null && _currentPage is not null)
        {
            var index = _currentNotebook.Pages.IndexOf(_currentPage);
            if (index >= 0 && MatchesPageFilter(_currentPage, index)) ids.Add(_currentPage.Id);
        }
        return ids;
    }

    private bool MovePages(IReadOnlySet<Guid> pageIds, int direction)
    {
        if (_currentNotebook is null || pageIds.Count == 0 || direction is not (-1 or 1)) return false;
        CaptureCurrentPage();
        var moved = false;
        if (direction < 0)
        {
            for (var index = 1; index < _currentNotebook.Pages.Count; index++)
            {
                if (!pageIds.Contains(_currentNotebook.Pages[index].Id) || pageIds.Contains(_currentNotebook.Pages[index - 1].Id)) continue;
                (_currentNotebook.Pages[index - 1], _currentNotebook.Pages[index]) = (_currentNotebook.Pages[index], _currentNotebook.Pages[index - 1]);
                moved = true;
            }
        }
        else
        {
            for (var index = _currentNotebook.Pages.Count - 2; index >= 0; index--)
            {
                if (!pageIds.Contains(_currentNotebook.Pages[index].Id) || pageIds.Contains(_currentNotebook.Pages[index + 1].Id)) continue;
                (_currentNotebook.Pages[index], _currentNotebook.Pages[index + 1]) = (_currentNotebook.Pages[index + 1], _currentNotebook.Pages[index]);
                moved = true;
            }
        }

        if (!moved)
        {
            StatusText.Text = direction < 0 ? "所选页面已在最前方" : "所选页面已在最后方";
            return false;
        }

        RefreshPageItems(_currentPage?.Id, pageIds);
        MarkDirty();
        StatusText.Text = pageIds.Count == 1 ? "页面顺序已调整" : $"已批量移动 {pageIds.Count} 个页面";
        return true;
    }

    private void PaperTemplateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || _isSwitchingPage || _currentPage is null || PaperTemplateCombo.SelectedItem is not ComboBoxItem { Tag: string paperTemplate }) return;
        _currentPage.PaperTemplate = paperTemplate;
        ApplyPageAppearance(_currentPage);
        UpdateCurrentPageThumbnail();
        MarkDirty();
        StatusText.Text = $"当前页已切换为{GetPaperTemplateDisplayName(paperTemplate)}";
    }

    private void PaperColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isLoadingNotebook || _isSwitchingPage || _currentPage is null || PaperColorCombo.SelectedItem is not ComboBoxItem { Tag: string paperColor }) return;
        _currentPage.PaperColor = paperColor;
        ApplyPageAppearance(_currentPage);
        UpdateCurrentPageThumbnail();
        MarkDirty();
        StatusText.Text = "当前页纸张颜色已更新";
    }

    private void ApplyPageAppearance(NotebookPage page)
    {
        var background = new SolidColorBrush(PageThumbnailService.ParsePaperColor(page.PaperColor));
        background.Freeze();
        PageHost.Background = background;
        PageTemplateLayer.Fill = page.PaperTemplate switch
        {
            "Blank" => Brushes.Transparent,
            "Lined" => (Brush)FindResource("LinedPaperBrush"),
            "Grid" => (Brush)FindResource("GridPaperBrush"),
            _ => (Brush)FindResource("DottedPaperBrush")
        };
        PageBackgroundImage.Source = PageBackgroundService.CreateImageSource(page);

        var wasSwitchingPage = _isSwitchingPage;
        _isSwitchingPage = true;
        try
        {
            SelectComboBoxItem(PaperTemplateCombo, page.PaperTemplate);
            SelectComboBoxItem(PaperColorCombo, page.PaperColor);
        }
        finally
        {
            _isSwitchingPage = wasSwitchingPage;
        }
    }

    private void UpdateCurrentPageThumbnail()
    {
        if (_currentPage is null) return;
        _currentPage.ModifiedAt = DateTimeOffset.Now;
        InvalidatePageThumbnail(_currentPage.Id);
        var item = _pageItems.FirstOrDefault(page => page.Id == _currentPage.Id);
        if (item is not null) item.Thumbnail = GetPageThumbnail(_currentPage);
        var overviewItem = _overviewPageItems.FirstOrDefault(page => page.Id == _currentPage.Id);
        if (overviewItem is not null) overviewItem.Thumbnail = GetPageThumbnail(_currentPage, 180, 255);
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static string GetPaperTemplateDisplayName(string paperTemplate) => paperTemplate switch
    {
        "Blank" => "空白纸",
        "Lined" => "横线纸",
        "Grid" => "方格纸",
        _ => "点阵纸"
    };

    private void AttachStrokeEvents(StrokeCollection strokes)
    {
        if (_observedStrokes is not null) _observedStrokes.StrokesChanged -= Strokes_StrokesChanged;
        _observedStrokes = strokes;
        _observedStrokes.StrokesChanged += Strokes_StrokesChanged;
    }

    private void Strokes_StrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_isRestoring || _isSwitchingPage) return;
        StatusText.Text = $"{_activeToolDisplayName()} · {InkSurface.Strokes.Count} 条笔迹";
        RecordActiveAudioCueForInk(e.Added);
        MarkDirty();
    }

    private void MarkDirty()
    {
        if (_isClosed || _isCloseRequested || _currentNotebook is null) return;
        _revision++;
        _isDirty = true;
        SaveStateText.Text = "未保存";
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private async void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        try { await SaveNotebookAsync(); } catch { }
    }

    private async Task SaveNotebookAsync()
    {
        await _notebookSaveGate.WaitAsync();
        try
        {
            if (!_isDirty || _currentNotebook is null || string.IsNullOrWhiteSpace(_currentNotebookPath)) return;
            var savingRevision = _revision;
            CaptureCurrentPage();
            var title = NotebookTitleBox.Text.Trim();
            _currentNotebook.Title = string.IsNullOrWhiteSpace(title) ? "未命名笔记本" : title;
            try
            {
                SaveStateText.Text = "正在保存…";
                await _notebookStorage.SaveAsync(_currentNotebook, _currentNotebookPath);
                if (_revision == savingRevision)
                {
                    _isDirty = false;
                    SaveStateText.Text = "已保存";
                }
                else
                {
                    SaveStateText.Text = "未保存";
                    if (!_isCloseRequested && !_isClosed)
                    {
                        _autosaveTimer.Stop();
                        _autosaveTimer.Start();
                    }
                }
            }
            catch (Exception exception)
            {
                SaveStateText.Text = "保存失败";
                StatusText.Text = $"自动保存失败：{exception.Message}";
                throw;
            }
        }
        finally
        {
            _notebookSaveGate.Release();
        }
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || sender is not RadioButton radioButton || radioButton.Tag is not string tool) return;
        _activeTool = tool;
        switch (_activeTool)
        {
            case "Pen": _currentColor = _penColor; SelectColorButton(_penColor); ThicknessSlider.Maximum = 20; ThicknessSlider.Value = _penThickness; break;
            case "Highlighter": _currentColor = _highlighterColor; SelectColorButton(_highlighterColor); ThicknessSlider.Maximum = 40; ThicknessSlider.Value = _highlighterThickness; break;
            case "Eraser": ThicknessSlider.Maximum = 48; ThicknessSlider.Value = _eraserSize; break;
        }
        if (!_isReadOnly) ApplyCurrentTool();
    }

    private void ApplyCurrentTool()
    {
        if (!_isInitialized || _isReadOnly) return;
        switch (_activeTool)
        {
            case "Pen":
                InkSurface.EditingMode = InkCanvasEditingMode.Ink;
                InkSurface.EditingModeInverted = InkCanvasEditingMode.EraseByStroke;
                InkSurface.DefaultDrawingAttributes = CreatePenDrawingAttributes();
                InkSurface.Cursor = Cursors.Pen;
                break;
            case "Highlighter":
                InkSurface.EditingMode = InkCanvasEditingMode.Ink;
                InkSurface.EditingModeInverted = InkCanvasEditingMode.EraseByStroke;
                var highlighter = CreateDrawingAttributes(_highlighterColor, _highlighterThickness, true);
                highlighter.StylusTip = StylusTip.Rectangle;
                InkSurface.DefaultDrawingAttributes = highlighter;
                InkSurface.Cursor = Cursors.Pen;
                break;
            case "Eraser":
                InkSurface.EditingMode = InkCanvasEditingMode.EraseByPoint;
                InkSurface.EditingModeInverted = InkCanvasEditingMode.EraseByStroke;
                InkSurface.EraserShape = new EllipseStylusShape(_eraserSize, _eraserSize);
                break;
            case "Select": InkSurface.EditingMode = InkCanvasEditingMode.Select; InkSurface.EditingModeInverted = InkCanvasEditingMode.EraseByStroke; break;
            default: InkSurface.EditingMode = InkCanvasEditingMode.None; InkSurface.EditingModeInverted = InkCanvasEditingMode.None; break;
        }
        if (EditorView.IsVisible) StatusText.Text = $"{_activeToolDisplayName()} · {InkSurface.Strokes.Count} 条笔迹";
    }

    private static DrawingAttributes CreateDrawingAttributes(Color color, double thickness, bool isHighlighter)
    {
        return new DrawingAttributes { Color = color, Width = thickness, Height = thickness, FitToCurve = true, IgnorePressure = false, IsHighlighter = isHighlighter, StylusTip = StylusTip.Ellipse };
    }

    private void Color_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || sender is not RadioButton radioButton || radioButton.Tag is not string colorText) return;
        _currentColor = (Color)ColorConverter.ConvertFromString(colorText);
        if (_activeTool == "Pen") { _penColor = _currentColor; ApplyCurrentTool(); }
        else if (_activeTool == "Highlighter") { _highlighterColor = _currentColor; ApplyCurrentTool(); }
    }

    private void SelectColorButton(Color color)
    {
        BlackColor.IsChecked = HasColor(BlackColor, color); BlueColor.IsChecked = HasColor(BlueColor, color); RedColor.IsChecked = HasColor(RedColor, color); GreenColor.IsChecked = HasColor(GreenColor, color); YellowColor.IsChecked = HasColor(YellowColor, color);
    }

    private static bool HasColor(RadioButton button, Color color)
    {
        return button.Tag is string colorText && ColorConverter.ConvertFromString(colorText) is Color buttonColor && buttonColor == color;
    }

    private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized) return;
        var value = Math.Round(e.NewValue, 1);
        ThicknessText.Text = value.ToString("0.0");
        switch (_activeTool) { case "Pen": _penThickness = value; break; case "Highlighter": _highlighterThickness = value; break; case "Eraser": _eraserSize = value; break; }
        ApplyCurrentTool();
    }

    private void InkSurface_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        if (TryBeginMixedLasso(e.GetPosition(InkSurface), true)) e.Handled = true;
        else BeginPointerAction();
    }

    private void InkSurface_PreviewStylusMove(object sender, StylusEventArgs e)
    {
        if (TryAppendMixedLassoPoint(e.GetPosition(InkSurface), true)) e.Handled = true;
    }

    private void InkSurface_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (TryFinishMixedLasso(e.GetPosition(InkSurface), true)) e.Handled = true;
        else EndPointerAction();
    }

    private void InkSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TryBeginMixedLasso(e.GetPosition(InkSurface), false)) e.Handled = true;
        else BeginPointerAction();
    }

    private void InkSurface_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && TryAppendMixedLassoPoint(e.GetPosition(InkSurface), false)) e.Handled = true;
    }

    private void InkSurface_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TryFinishMixedLasso(e.GetPosition(InkSurface), false)) e.Handled = true;
        else EndPointerAction();
    }

    private void BeginPointerAction()
    {
        if (_isPointerActionActive || _isReadOnly || _activeTool is "Select" or "Pan") return;
        _history.Record(InkSurface.Strokes); _isPointerActionActive = true; UpdateHistoryButtons();
    }
    private void EndPointerAction() => _isPointerActionActive = false;
    private void InkSurface_SelectionMoving(object sender, InkCanvasSelectionEditingEventArgs e) => BeginMixedSelectionEdit(e, resize: false);
    private void InkSurface_SelectionMoved(object sender, EventArgs e) => EndMixedSelectionEdit();
    private void InkSurface_SelectionResizing(object sender, InkCanvasSelectionEditingEventArgs e) => BeginMixedSelectionEdit(e, resize: true);
    private void InkSurface_SelectionResized(object sender, EventArgs e) => EndMixedSelectionEdit();

    private void BeginSelectionAction()
    {
        if (_isSelectionActionActive || _isReadOnly) return;
        _history.Record(InkSurface.Strokes); _isSelectionActionActive = true; UpdateHistoryButtons();
    }
    private void EndSelectionAction() { _isSelectionActionActive = false; MarkDirty(); }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var restored = _history.Undo(InkSurface.Strokes); if (restored is null) return; ReplaceStrokes(restored, true); UpdateHistoryButtons();
    }
    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        var restored = _history.Redo(InkSurface.Strokes); if (restored is null) return; ReplaceStrokes(restored, true); UpdateHistoryButtons();
    }

    private void ReplaceStrokes(StrokeCollection strokes, bool markDirty)
    {
        _isRestoring = true;
        try { InkSurface.Strokes = strokes; AttachStrokeEvents(strokes); }
        finally { _isRestoring = false; }
        if (markDirty) MarkDirty();
        StatusText.Text = $"{_activeToolDisplayName()} · {InkSurface.Strokes.Count} 条笔迹";
    }

    private void UpdateHistoryButtons() { UndoButton.IsEnabled = _history.CanUndo; RedoButton.IsEnabled = _history.CanRedo; }

    private async void SaveNow_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null) return;
        if (!_isDirty) { _revision++; _isDirty = true; }
        _autosaveTimer.Stop();
        try { await SaveNotebookAsync(); StatusText.Text = "笔记本已保存到本机"; }
        catch (Exception exception) { MessageBox.Show(this, $"无法保存笔记本。\n\n{exception.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void OpenInk_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNotebook is null) return;
        var dialog = new OpenFileDialog { Title = "导入 ISF 笔迹到当前页", Filter = "Ink Serialized Format (*.isf)|*.isf|所有文件 (*.*)|*.*", InitialDirectory = Directory.Exists(_storage.NotesDirectory) ? _storage.NotesDirectory : null };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var strokes = await _storage.LoadAsync(dialog.FileName); ReplaceStrokes(strokes, true); _history.Clear(); UpdateHistoryButtons(); StatusText.Text = $"已导入 {Path.GetFileName(dialog.FileName)} 到当前页";
        }
        catch (Exception exception) { MessageBox.Show(this, $"无法导入笔迹文件。\n\n{exception.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ExportInk_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "导出当前页可编辑笔迹", Filter = "Ink Serialized Format (*.isf)|*.isf", FileName = $"PaperNote-{DateTime.Now:yyyyMMdd-HHmm}.isf", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (dialog.ShowDialog(this) != true) return;
        try { await _storage.SaveBytesAsync(InkHistoryService.Serialize(InkSurface.Strokes), dialog.FileName); StatusText.Text = $"已导出 {Path.GetFileName(dialog.FileName)}"; }
        catch (Exception exception) { MessageBox.Show(this, $"无法导出笔迹。\n\n{exception.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ClearPage_Click(object sender, RoutedEventArgs e)
    {
        if (InkSurface.Strokes.Count == 0) return;
        var result = MessageBox.Show(this, "确定清空当前页面的全部笔迹吗？可以使用撤销恢复。", "清空页面", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _history.Record(InkSurface.Strokes); InkSurface.Strokes.Clear(); UpdateHistoryButtons();
    }

    private void ToggleReadOnly_Click(object sender, RoutedEventArgs e)
    {
        _isReadOnly = !_isReadOnly; ReadOnlyButtonText.Text = _isReadOnly ? "继续编辑" : "只读";
        ImportPdfButton.IsEnabled = !_isReadOnly;
        PageTitleBox.IsReadOnly = _isReadOnly;
        CurrentPageBookmarkButton.IsEnabled = !_isReadOnly;
        SetPageObjectsReadOnly(_isReadOnly);
        if (_isReadOnly) { InkSurface.EditingMode = InkCanvasEditingMode.None; InkSurface.EditingModeInverted = InkCanvasEditingMode.None; StatusText.Text = "只读模式 · 可缩放和浏览"; }
        else ApplyCurrentTool();
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => SidebarColumn.Width = SidebarColumn.Width.Value > 0 ? new GridLength(0) : new GridLength(230);
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_isInitialized) ApplyZoom(); }
    private void ApplyZoom() { var zoom = ZoomSlider.Value / 100d; PageScale.ScaleX = zoom; PageScale.ScaleY = zoom; ZoomText.Text = $"{Math.Round(ZoomSlider.Value):0}%"; }

    private void CanvasScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + (e.Delta > 0 ? 10 : -10), ZoomSlider.Minimum, ZoomSlider.Maximum); e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!EditorView.IsVisible) return;
        var shortcutKey = GetShortcutKey(e);
        var isTextEditing = Keyboard.FocusedElement is TextBox;
        var modifiers = Keyboard.Modifiers;
        if (shortcutKey == Key.Escape)
        {
            if (PdfCropEditorOverlay.Visibility == Visibility.Visible) ClosePdfCropEditor();
            else if (PdfImportOptionsOverlay.Visibility == Visibility.Visible) ClosePdfImportOptions();
            else if (PdfExportOptionsOverlay.Visibility == Visibility.Visible) ClosePdfExportOptions();
            else if (OutlineOverlay.Visibility == Visibility.Visible) CloseOutline();
            else if (PageOverviewOverlay.Visibility == Visibility.Visible) ClosePageOverview();
            else goto ContinueShortcutHandling;
            e.Handled = true;
            return;
        }

    ContinueShortcutHandling:
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            switch (shortcutKey)
            {
                case Key.C when !isTextEditing: CopySelectedPageObjects(); e.Handled = true; return;
                case Key.X when !isTextEditing: CutSelectedPageObjects(); e.Handled = true; return;
                case Key.V when !isTextEditing: PastePageObjects(); e.Handled = true; return;
                case Key.O when !isTextEditing && (modifiers & ModifierKeys.Shift) != 0: OpenPageOverview(); e.Handled = true; return;
                case Key.G when !isTextEditing && (modifiers & ModifierKeys.Shift) != 0: UngroupSelectedPageObjects(); e.Handled = true; return;
                case Key.L when !isTextEditing && (modifiers & ModifierKeys.Shift) != 0: UnlockSelectedPageObjects(); e.Handled = true; return;
                case Key.L when !isTextEditing: LockSelectedPageObjects(); e.Handled = true; return;
                case Key.G when !isTextEditing: GroupSelectedPageObjects(); e.Handled = true; return;
                case Key.J when !isTextEditing: FocusPageJumpBox(); e.Handled = true; return;
                case Key.Left when !isTextEditing && (modifiers & ModifierKeys.Alt) != 0: RotateSelectedPageObjectsLeft(); e.Handled = true; return;
                case Key.Right when !isTextEditing && (modifiers & ModifierKeys.Alt) != 0: RotateSelectedPageObjectsRight(); e.Handled = true; return;
                case Key.OemCloseBrackets when !isTextEditing && (modifiers & ModifierKeys.Alt) != 0: BringSelectedPageObjectsForward(); e.Handled = true; return;
                case Key.OemOpenBrackets when !isTextEditing && (modifiers & ModifierKeys.Alt) != 0: SendSelectedPageObjectsBackward(); e.Handled = true; return;
                case Key.OemCloseBrackets when !isTextEditing: BringSelectedPageObjectsToFront(); e.Handled = true; return;
                case Key.OemOpenBrackets when !isTextEditing: SendSelectedPageObjectsToBack(); e.Handled = true; return;
                case Key.Z: Undo_Click(sender, e); e.Handled = true; return;
                case Key.Y: Redo_Click(sender, e); e.Handled = true; return;
                case Key.S: SaveNow_Click(sender, e); e.Handled = true; return;
                case Key.Tab when !isTextEditing: CycleNotebookTabs((modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); e.Handled = true; return;
                case Key.W when !isTextEditing: CloseCurrentNotebookTabShortcut(); e.Handled = true; return;
                case Key.D when !isTextEditing && (modifiers & ModifierKeys.Shift) != 0: OpenOutline(); e.Handled = true; return;
                case Key.D when !isTextEditing && _currentPage is not null: DuplicatePage(_currentPage.Id); e.Handled = true; return;
                case Key.Enter when !isTextEditing: InsertBlankPage(_currentPage?.PaperTemplate ?? PaperPageDefaults.Template, _currentPage?.PaperColor ?? PaperPageDefaults.Color, "AfterCurrent"); e.Handled = true; return;
                case Key.A when _activeTool == "Select" && !_isReadOnly:
                    SelectAllInkAndPageObjects(); e.Handled = true; return;
            }
        }
        if (isTextEditing) return;
        switch (shortcutKey)
        {
            case Key.P: PenTool.IsChecked = true; e.Handled = true; break;
            case Key.H: HighlighterTool.IsChecked = true; e.Handled = true; break;
            case Key.E: EraserTool.IsChecked = true; e.Handled = true; break;
            case Key.L: SelectTool.IsChecked = true; e.Handled = true; break;
            case Key.R: ToggleReadOnly_Click(sender, e); e.Handled = true; break;
            case Key.F11: ToggleFullscreen(); e.Handled = true; break;
            case Key.PageUp: NavigateRelativePage(-1); e.Handled = true; break;
            case Key.PageDown: NavigateRelativePage(1); e.Handled = true; break;
            case Key.BrowserBack: NavigatePageHistory(true); e.Handled = true; break;
            case Key.BrowserForward: NavigatePageHistory(false); e.Handled = true; break;
            case Key.Delete: DeleteSelection(); e.Handled = true; break;
            case Key.Escape: ClearMixedSelection(); e.Handled = true; break;
        }
    }

    private static Key GetShortcutKey(KeyEventArgs e) => e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, Key.DeadCharProcessed => e.DeadCharProcessedKey, _ => e.Key };

    private void DeleteSelection()
    {
        DeleteMixedSelection();
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _previousWindowStyle = WindowStyle; _previousWindowState = WindowState; _previousResizeMode = ResizeMode; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; _isFullscreen = true;
        }
        else
        {
            WindowStyle = _previousWindowStyle; ResizeMode = _previousResizeMode; WindowState = _previousWindowState; _isFullscreen = false;
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;

        e.Cancel = true;
        if (_isCloseRequested) return;

        _isCloseRequested = true;
        _autosaveTimer.Stop();
        LibraryView.IsEnabled = false;
        EditorView.IsEnabled = false;
        SaveStateText.Text = "正在安全退出…";

        try
        {
            await PrepareForCloseAsync();
        }
        catch (Exception exception)
        {
            var result = MessageBox.Show(this, $"退出前保存失败：\n\n{exception.Message}\n\n仍然退出吗？", "保存失败", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                _isCloseRequested = false;
                LibraryView.IsEnabled = true;
                EditorView.IsEnabled = true;
                if (_isDirty)
                {
                    _autosaveTimer.Stop();
                    _autosaveTimer.Start();
                }
                return;
            }
        }

        _allowClose = true;
        Close();
    }

    private async Task PrepareForCloseAsync()
    {
        StopAudioForContextChange();
        _autosaveTimer.Stop();
        await SaveNotebookAsync();
        if (!IsLoaded) return;

        try
        {
            await SaveWorkspaceStateAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"工作区状态保存失败：{exception.Message}";
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _autosaveTimer.Stop();
        _autosaveTimer.Tick -= AutosaveTimer_Tick;
        ShutdownDesktopAudio();
        if (_observedStrokes is not null)
        {
            _observedStrokes.StrokesChanged -= Strokes_StrokesChanged;
            _observedStrokes = null;
        }
    }

    private string _activeToolDisplayName() => _activeTool switch { "Pen" => GetPenProfileDisplayName(_penProfile), "Highlighter" => "荧光笔", "Eraser" => "橡皮", "Select" => "套索", "Pan" => "浏览", _ => _activeTool };
}
