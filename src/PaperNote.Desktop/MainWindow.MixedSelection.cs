using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Desktop.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private readonly List<Point> _mixedLassoPoints = [];
    private bool _isMixedLassoActive;
    private bool _mixedLassoUsesStylus;
    private ModifierKeys _mixedLassoModifiers;
    private PageSelectionFilter _mixedSelectionFilter = PageSelectionFilter.All;
    private readonly Dictionary<Guid, PageObject> _mixedSelectionObjectOrigins = [];
    private Rect _mixedSelectionInkOrigin;
    private bool _mixedSelectionEditActive;
    private bool _mixedSelectionEditIsResize;

    private bool SelectAllInkAndPageObjects()
    {
        if (_isReadOnly || _currentPage is null || _activeTool != "Select") return false;
        SyncCurrentInkModelForSelection();
        var polygon = new[]
        {
            InkPoint(-1, -1),
            InkPoint(PageThumbnailService.PageWidth + 1, -1),
            InkPoint(PageThumbnailService.PageWidth + 1, PageThumbnailService.PageHeight + 1),
            InkPoint(-1, PageThumbnailService.PageHeight + 1)
        };
        ApplySelectionResult(PageSelectionService.SelectByPolygon(_currentPage, polygon, _mixedSelectionFilter), additive: false);
        UpdateMixedSelectionStatus();
        return HasMixedSelection();
    }

    private bool TryBeginMixedLasso(Point point, bool usesStylus)
    {
        if (_isMixedLassoActive) return _mixedLassoUsesStylus == usesStylus;
        if (_isReadOnly || _currentPage is null || _activeTool != "Select") return false;

        var selectionBounds = InkSurface.GetSelectionBounds();
        if (InkSurface.GetSelectedStrokes().Count > 0 && !selectionBounds.IsEmpty)
        {
            selectionBounds.Inflate(8, 8);
            if (selectionBounds.Contains(point)) return false;
        }

        _mixedLassoPoints.Clear();
        _mixedLassoPoints.Add(ClampToPage(point));
        _mixedLassoUsesStylus = usesStylus;
        _mixedLassoModifiers = Keyboard.Modifiers;
        _isMixedLassoActive = true;
        MixedLassoLine.Points.Clear();
        MixedLassoLine.Points.Add(_mixedLassoPoints[0]);
        MixedLassoLine.Visibility = Visibility.Visible;
        if (usesStylus) InkSurface.CaptureStylus();
        else InkSurface.CaptureMouse();
        return true;
    }

    private bool TryAppendMixedLassoPoint(Point point, bool usesStylus)
    {
        if (!_isMixedLassoActive || _mixedLassoUsesStylus != usesStylus) return false;
        point = ClampToPage(point);
        if (_mixedLassoPoints.Count == 0 || (point - _mixedLassoPoints[^1]).Length >= 3)
        {
            _mixedLassoPoints.Add(point);
            MixedLassoLine.Points.Add(point);
        }
        return true;
    }

    private bool TryFinishMixedLasso(Point point, bool usesStylus)
    {
        if (!_isMixedLassoActive || _mixedLassoUsesStylus != usesStylus) return false;
        TryAppendMixedLassoPoint(point, usesStylus);
        if (usesStylus) InkSurface.ReleaseStylusCapture();
        else InkSurface.ReleaseMouseCapture();
        _isMixedLassoActive = false;

        try
        {
            if (_mixedLassoPoints.Count < 3 || GetLassoBounds(_mixedLassoPoints).Width < 4 || GetLassoBounds(_mixedLassoPoints).Height < 4)
            {
                if ((_mixedLassoModifiers & ModifierKeys.Control) == 0) ClearMixedSelection();
                return true;
            }

            ApplyMixedLassoSelection(_mixedLassoPoints, (_mixedLassoModifiers & ModifierKeys.Control) != 0);
            return true;
        }
        finally
        {
            MixedLassoLine.Visibility = Visibility.Collapsed;
            MixedLassoLine.Points.Clear();
            _mixedLassoPoints.Clear();
        }
    }

    private void ApplyMixedLassoSelection(IReadOnlyList<Point> lassoPoints, bool additive)
    {
        if (_currentPage is null) return;
        SyncCurrentInkModelForSelection();
        var polygon = lassoPoints.Select(point => InkPoint(point.X, point.Y)).ToArray();
        var result = PageSelectionService.SelectByPolygon(_currentPage, polygon, _mixedSelectionFilter);
        ApplySelectionResult(result, additive);
        UpdateMixedSelectionStatus();
    }

    private void ApplySelectionResult(PageSelectionResult result, bool additive)
    {
        var strokeIds = result.StrokeIds.Where(id =>
        {
            var stroke = _currentPage?.Ink.Strokes.FirstOrDefault(item => item.Id == id);
            return stroke is not null && !PageLayerService.IsContentLocked(_currentPage!, stroke.LayerId);
        }).ToHashSet();
        var selectedStrokes = additive
            ? new StrokeCollection(InkSurface.GetSelectedStrokes().Where(stroke =>
            {
                var model = _currentPage?.Ink.Strokes.FirstOrDefault(item => item.Id == WpfInkAdapter.GetStrokeId(stroke));
                return model is not null && !PageLayerService.IsContentLocked(_currentPage!, model.LayerId);
            }))
            : new StrokeCollection();
        foreach (var stroke in InkSurface.Strokes)
        {
            if (strokeIds.Contains(WpfInkAdapter.GetStrokeId(stroke)) && !selectedStrokes.Contains(stroke)) selectedStrokes.Add(stroke);
        }
        InkSurface.Select(selectedStrokes);

        if (!additive) _selectedPageObjectIds.Clear();
        foreach (var id in result.ObjectIds) _selectedPageObjectIds.Add(id);
        SetPrimarySelectionFromCurrentIds();
        UpdatePageObjectSelectionVisuals();
    }

    private static PaperInkPoint InkPoint(double x, double y) => new() { X = x, Y = y };

    private static Rect GetLassoBounds(IReadOnlyList<Point> points)
    {
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        return new Rect(new Point(minX, minY), new Point(maxX, maxY));
    }

    private static Point ClampToPage(Point point)
    {
        return new Point(
            Math.Clamp(point.X, 0, PageThumbnailService.PageWidth),
            Math.Clamp(point.Y, 0, PageThumbnailService.PageHeight));
    }

    private void BeginMixedSelectionEdit(InkCanvasSelectionEditingEventArgs e, bool resize)
    {
        BeginSelectionAction();
        if (_currentPage is null || _selectedPageObjectIds.Count == 0) return;
        if (!_mixedSelectionEditActive || _mixedSelectionEditIsResize != resize)
        {
            _mixedSelectionEditActive = true;
            _mixedSelectionEditIsResize = resize;
            _mixedSelectionInkOrigin = e.OldRectangle;
            _mixedSelectionObjectOrigins.Clear();
            foreach (var item in SelectedPageObjects().Where(IsPageObjectEditable)) _mixedSelectionObjectOrigins[item.Id] = item.Clone();
        }
        if (_mixedSelectionObjectOrigins.Count == 0 || _mixedSelectionInkOrigin.IsEmpty) return;

        if (!resize)
        {
            var deltaX = e.NewRectangle.X - _mixedSelectionInkOrigin.X;
            var deltaY = e.NewRectangle.Y - _mixedSelectionInkOrigin.Y;
            foreach (var (id, origin) in _mixedSelectionObjectOrigins)
            {
                if (_currentPage.Objects.FirstOrDefault(item => item.Id == id) is not { } item) continue;
                item.X = Math.Clamp(origin.X + deltaX, 0, Math.Max(0, PageThumbnailService.PageWidth - item.Width));
                item.Y = Math.Clamp(origin.Y + deltaY, 0, Math.Max(0, PageThumbnailService.PageHeight - item.Height));
                UpdateMixedSelectionObjectContainer(item);
            }
            return;
        }

        var scaleX = e.NewRectangle.Width / Math.Max(_mixedSelectionInkOrigin.Width, .001);
        var scaleY = e.NewRectangle.Height / Math.Max(_mixedSelectionInkOrigin.Height, .001);
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY)) return;
        foreach (var (id, origin) in _mixedSelectionObjectOrigins)
        {
            if (_currentPage.Objects.FirstOrDefault(item => item.Id == id) is not { } item) continue;
            var minWidth = item.Kind == "Text" ? 150 : 80;
            var minHeight = item.Kind == "Text" ? 90 : 60;
            item.X = Math.Clamp(e.NewRectangle.X + (origin.X - _mixedSelectionInkOrigin.X) * scaleX, 0, PageThumbnailService.PageWidth - minWidth);
            item.Y = Math.Clamp(e.NewRectangle.Y + (origin.Y - _mixedSelectionInkOrigin.Y) * scaleY, 0, PageThumbnailService.PageHeight - minHeight);
            item.Width = Math.Clamp(origin.Width * scaleX, minWidth, PageThumbnailService.PageWidth - item.X);
            item.Height = Math.Clamp(origin.Height * scaleY, minHeight, PageThumbnailService.PageHeight - item.Y);
            UpdateMixedSelectionObjectContainer(item);
        }
    }

    private void EndMixedSelectionEdit()
    {
        var objectsChanged = _mixedSelectionEditActive && _mixedSelectionObjectOrigins.Count > 0;
        _mixedSelectionEditActive = false;
        _mixedSelectionObjectOrigins.Clear();
        EndSelectionAction();
        if (objectsChanged) PageObjectChanged();
        UpdateMixedSelectionStatus();
    }

    private bool IsPageObjectEditable(PageObject item)
    {
        return _currentPage is not null && !item.IsLocked && !PageLayerService.IsContentLocked(_currentPage, item.LayerId);
    }

    private void UpdateMixedSelectionObjectContainer(PageObject item)
    {
        if (!_pageObjectContainers.TryGetValue(item.Id, out var container)) return;
        Canvas.SetLeft(container, item.X);
        Canvas.SetTop(container, item.Y);
        container.Width = item.Width;
        container.Height = item.Height;
    }

    private void SelectionActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = CreateSelectionActionsMenu();
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu CreateSelectionActionsMenu()
    {
        var hasInk = InkSurface.GetSelectedStrokes().Count > 0;
        var hasObjects = _selectedPageObjectIds.Count > 0;
        var hasSelection = hasInk || hasObjects;
        var canModify = hasSelection && !_isReadOnly;
        var menu = new ContextMenu();

        var filterMenu = new MenuItem { Header = $"套索筛选：{MixedSelectionFilterName(_mixedSelectionFilter)}" };
        AddSelectionFilterItem(filterMenu, "全部内容", PageSelectionFilter.All);
        AddSelectionFilterItem(filterMenu, "全部笔迹", PageSelectionFilter.Ink);
        AddSelectionFilterItem(filterMenu, "仅钢笔", PageSelectionFilter.Pen);
        AddSelectionFilterItem(filterMenu, "仅荧光笔", PageSelectionFilter.Highlighter);
        filterMenu.Items.Add(new Separator());
        AddSelectionFilterItem(filterMenu, "全部对象", PageSelectionFilter.Objects);
        AddSelectionFilterItem(filterMenu, "仅文字", PageSelectionFilter.Text);
        AddSelectionFilterItem(filterMenu, "仅图片", PageSelectionFilter.Image);
        AddSelectionFilterItem(filterMenu, "仅形状", PageSelectionFilter.Shape);
        menu.Items.Add(filterMenu);
        menu.Items.Add(new Separator());

        menu.Items.Add(CreateMenuItem("改为当前颜色", "", (_, _) => ApplyMixedSelectionStyle(color: ColorToHex(_currentColor)), canModify));
        var opacityMenu = new MenuItem { Header = "设置透明度", IsEnabled = canModify };
        foreach (var (label, value) in new[] { ("100%", 1d), ("75%", .75d), ("50%", .5d), ("35%", .35d), ("25%", .25d) })
            opacityMenu.Items.Add(CreateMenuItem(label, "", (_, _) => ApplyMixedSelectionStyle(opacity: value), canModify));
        menu.Items.Add(opacityMenu);

        var widthMenu = new MenuItem { Header = "设置笔迹粗细", IsEnabled = hasInk && !_isReadOnly };
        foreach (var value in new[] { 1d, 2d, 3.2d, 5d, 8d, 12d, 18d, 24d })
        {
            var captured = value;
            widthMenu.Items.Add(CreateMenuItem(captured.ToString("0.#"), "", (_, _) => ApplyMixedSelectionStyle(width: captured), hasInk && !_isReadOnly));
        }
        menu.Items.Add(widthMenu);

        var toolMenu = new MenuItem { Header = "设置笔迹类型", IsEnabled = hasInk && !_isReadOnly };
        toolMenu.Items.Add(CreateMenuItem("钢笔", "", (_, _) => ApplyMixedSelectionStyle(tool: PaperInkTool.Pen), hasInk && !_isReadOnly));
        toolMenu.Items.Add(CreateMenuItem("荧光笔", "", (_, _) => ApplyMixedSelectionStyle(tool: PaperInkTool.Highlighter), hasInk && !_isReadOnly));
        menu.Items.Add(toolMenu);
        menu.Items.Add(CreateMenuItem("跳到关联录音", "", (_, _) => JumpSelectedInkToAudio(), hasInk));
        menu.Items.Add(new Separator());

        menu.Items.Add(CreateMenuItem("复制一份", "", (_, _) => DuplicateMixedSelection(), canModify));
        menu.Items.Add(CreateMenuItem("导出选区为 PNG…", "", (_, _) => ExportMixedSelectionPng(), hasSelection));
        menu.Items.Add(CreateMenuItem("向左旋转 90°", "Alt+Left", (_, _) => RotateMixedSelection(-90), canModify));
        menu.Items.Add(CreateMenuItem("向右旋转 90°", "Alt+Right", (_, _) => RotateMixedSelection(90), canModify));
        menu.Items.Add(CreateSelectionTransferMenu("复制到其他页面", move: false, canModify));
        menu.Items.Add(CreateSelectionTransferMenu("移动到其他页面", move: true, canModify));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("清除选择", "Esc", (_, _) => ClearMixedSelection(), hasSelection));
        menu.Items.Add(CreateMenuItem("删除选中内容", "Delete", (_, _) => DeleteMixedSelection(), canModify));
        return menu;
    }

    private void AddSelectionFilterItem(ItemsControl menu, string label, PageSelectionFilter filter)
    {
        var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _mixedSelectionFilter == filter };
        item.Click += (_, _) => SetMixedSelectionFilter(filter);
        menu.Items.Add(item);
    }

    private MenuItem CreateSelectionTransferMenu(string header, bool move, bool enabled)
    {
        var menu = new MenuItem { Header = header, IsEnabled = enabled && _currentNotebook is { Pages.Count: > 1 } };
        if (_currentNotebook is null || _currentPage is null) return menu;
        for (var index = 0; index < _currentNotebook.Pages.Count; index++)
        {
            var page = _currentNotebook.Pages[index];
            if (page.Id == _currentPage.Id) continue;
            var target = page;
            var title = string.IsNullOrWhiteSpace(page.Title) ? string.Empty : $" · {page.Title}";
            menu.Items.Add(CreateMenuItem($"第 {index + 1} 页{title}", "", (_, _) => TransferMixedSelection(target, move), enabled));
        }
        return menu;
    }

    private void SetMixedSelectionFilter(PageSelectionFilter filter)
    {
        if (_mixedSelectionFilter == filter) return;
        _mixedSelectionFilter = filter;
        ClearMixedSelection();
        StatusText.Text = $"套索筛选已切换为：{MixedSelectionFilterName(filter)}";
    }

    private static string MixedSelectionFilterName(PageSelectionFilter filter) => filter switch
    {
        PageSelectionFilter.Ink => "全部笔迹",
        PageSelectionFilter.Pen => "仅钢笔",
        PageSelectionFilter.Highlighter => "仅荧光笔",
        PageSelectionFilter.Objects => "全部对象",
        PageSelectionFilter.Text => "仅文字",
        PageSelectionFilter.Image => "仅图片",
        PageSelectionFilter.Shape => "仅形状",
        _ => "全部内容"
    };

    private bool ApplyMixedSelectionStyle(string? color = null, double? width = null, double? opacity = null, PaperInkTool? tool = null)
    {
        if (_isReadOnly || _currentPage is null) return false;
        var strokeIds = GetSelectedStrokeIds();
        var objectIds = _selectedPageObjectIds.ToArray();
        if (strokeIds.Count == 0 && objectIds.Length == 0) return false;

        _history.Record(InkSurface.Strokes);
        CaptureCurrentPage();
        var inkChanged = InkSelectionService.UpdateStyle(_currentPage, strokeIds, color, width, opacity, tool);
        var objectChanged = (color is not null || opacity.HasValue) &&
                            PageObjectEditingService.UpdateStyle(_currentPage, objectIds, strokeColor: color, opacity: opacity);
        if (!inkChanged && !objectChanged) return false;

        RefreshCurrentPageFromModel(strokeIds, objectIds);
        UpdateHistoryButtons();
        StatusText.Text = "已更新选中内容样式";
        return true;
    }

    private bool ExportMixedSelectionPng()
    {
        if (_currentPage is null || !HasMixedSelection()) return false;
        SyncCurrentInkModelForSelection();
        var selection = SelectionExportService.Create(_currentPage, GetSelectedStrokeIds(), _selectedPageObjectIds);
        if (selection is null) return false;
        var dialog = new SaveFileDialog
        {
            Title = "导出选区为 PNG",
            Filter = "PNG 图片|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"PaperNote-Selection-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };
        if (dialog.ShowDialog(this) != true) return false;
        try
        {
            const double scale = 2;
            var bitmap = PageThumbnailService.CreatePageBitmap(selection.Page, 1680, 2376);
            var x = Math.Clamp((int)Math.Floor(selection.X * scale), 0, bitmap.PixelWidth - 1);
            var y = Math.Clamp((int)Math.Floor(selection.Y * scale), 0, bitmap.PixelHeight - 1);
            var width = Math.Clamp((int)Math.Ceiling(selection.Width * scale), 1, bitmap.PixelWidth - x);
            var height = Math.Clamp((int)Math.Ceiling(selection.Height * scale), 1, bitmap.PixelHeight - y);
            var crop = new CroppedBitmap(bitmap, new Int32Rect(x, y, width, height));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(crop));
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            StatusText.Text = $"选区已导出：{Path.GetFileName(dialog.FileName)}";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, $"无法导出选区。\n\n{exception.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool DuplicateMixedSelection()
    {
        if (_isReadOnly || _currentPage is null) return false;
        var strokeIds = GetSelectedStrokeIds();
        var objectIds = SelectedPageObjects().Where(item => !item.IsLocked).Select(item => item.Id).ToArray();
        if (strokeIds.Count == 0 && objectIds.Length == 0) return false;

        _history.Record(InkSurface.Strokes);
        CaptureCurrentPage();
        var newStrokeIds = InkSelectionService.Duplicate(_currentPage, strokeIds);
        var newObjectIds = PageObjectEditingService.Duplicate(_currentPage, objectIds);
        RefreshCurrentPageFromModel(newStrokeIds, newObjectIds);
        UpdateHistoryButtons();
        StatusText.Text = $"已复制 {newStrokeIds.Count} 条笔迹和 {newObjectIds.Count} 个对象";
        return newStrokeIds.Count + newObjectIds.Count > 0;
    }

    private bool RotateMixedSelection(double degrees)
    {
        if (_isReadOnly || _currentPage is null) return false;
        var strokeIds = GetSelectedStrokeIds();
        var objectIds = _selectedPageObjectIds.ToArray();
        if (strokeIds.Count == 0 && objectIds.Length == 0) return false;

        _history.Record(InkSurface.Strokes);
        CaptureCurrentPage();
        if (!PageSelectionService.Rotate(_currentPage, strokeIds, objectIds, degrees)) return false;
        RefreshCurrentPageFromModel(strokeIds, objectIds);
        UpdateHistoryButtons();
        StatusText.Text = degrees < 0 ? "已向左旋转选中内容" : "已向右旋转选中内容";
        return true;
    }

    private bool TransferMixedSelection(NotebookPage targetPage, bool move)
    {
        if (_isReadOnly || _currentPage is null || targetPage.Id == _currentPage.Id) return false;
        var strokeIds = GetSelectedStrokeIds();
        var objectIds = _selectedPageObjectIds.ToArray();
        if (strokeIds.Count == 0 && objectIds.Length == 0) return false;

        _history.Record(InkSurface.Strokes);
        CaptureCurrentPage();
        var result = PageSelectionService.Transfer(_currentPage, targetPage, strokeIds, objectIds, move);
        if (result.Count == 0) return false;

        InvalidatePageThumbnail(targetPage.Id);
        RefreshCurrentPageFromModel(move ? Array.Empty<Guid>() : strokeIds, move ? Array.Empty<Guid>() : objectIds);
        RefreshPageItems(_currentPage.Id);
        UpdateHistoryButtons();
        StatusText.Text = $"已{(move ? "移动" : "复制")} {result.StrokeIds.Count} 条笔迹和 {result.ObjectIds.Count} 个对象到其他页面";
        return true;
    }

    private void RefreshCurrentPageFromModel(IEnumerable<Guid> selectedStrokeIds, IEnumerable<Guid> selectedObjectIds)
    {
        if (_currentPage is null) return;
        var strokeSet = selectedStrokeIds.ToHashSet();
        var objectSet = selectedObjectIds.ToHashSet();
        _currentPage.InkData = PageThumbnailService.Serialize(WpfInkAdapter.ToStrokeCollection(_currentPage.Ink));
        ReplaceStrokes(WpfInkAdapter.ToStrokeCollection(_currentPage.Ink), false);
        LoadPageObjects(_currentPage);
        InkSurface.Select(new StrokeCollection(InkSurface.Strokes.Where(stroke => strokeSet.Contains(WpfInkAdapter.GetStrokeId(stroke)))));
        foreach (var id in objectSet.Where(id => _currentPage.Objects.Any(item => item.Id == id))) _selectedPageObjectIds.Add(id);
        SetPrimarySelectionFromCurrentIds();
        UpdatePageObjectSelectionVisuals();
        _currentPage.ModifiedAt = DateTimeOffset.Now;
        UpdateCurrentPageThumbnail();
        MarkDirty();
    }

    private void SyncCurrentInkModelForSelection()
    {
        if (_currentPage is not null) _currentPage.Ink = WpfInkAdapter.ToPaperInk(InkSurface.Strokes);
    }

    private IReadOnlyList<Guid> GetSelectedStrokeIds()
    {
        return InkSurface.GetSelectedStrokes().Select(WpfInkAdapter.GetStrokeId).Distinct().ToArray();
    }

    private bool HasMixedSelection() => InkSurface.GetSelectedStrokes().Count > 0 || _selectedPageObjectIds.Count > 0;

    private void ClearMixedSelection()
    {
        InkSurface.Select(new StrokeCollection());
        ClearPageObjectSelection();
        StatusText.Text = "已清除选择";
    }

    private bool DeleteMixedSelection()
    {
        if (_isReadOnly || _currentPage is null) return false;
        var strokeIds = GetSelectedStrokeIds();
        var objectIds = _selectedPageObjectIds.ToArray();
        if (strokeIds.Count == 0 && objectIds.Length == 0) return false;

        var lockedStrokeCount = strokeIds.Count(id =>
        {
            var stroke = _currentPage.Ink.Strokes.FirstOrDefault(item => item.Id == id);
            return stroke is not null && PageLayerService.IsContentLocked(_currentPage, stroke.LayerId);
        });
        var lockedObjectCount = SelectedPageObjects().Count(item => item.IsLocked || PageLayerService.IsContentLocked(_currentPage, item.LayerId));

        _history.Record(InkSurface.Strokes);
        CaptureCurrentPage();
        var deletedStrokes = InkSelectionService.Delete(_currentPage, strokeIds);
        var beforeObjects = _currentPage.Objects.Count;
        PageObjectEditingService.Delete(_currentPage, objectIds);
        var deletedObjects = beforeObjects - _currentPage.Objects.Count;
        if (deletedStrokes + deletedObjects == 0) return false;

        RefreshCurrentPageFromModel([], []);
        UpdateHistoryButtons();
        var kept = lockedStrokeCount + lockedObjectCount;
        StatusText.Text = $"已删除 {deletedStrokes} 条笔迹和 {deletedObjects} 个对象" +
                          (kept > 0 ? $"，保留 {kept} 项锁定内容" : string.Empty);
        return true;
    }

    private void UpdateMixedSelectionStatus()
    {
        var inkCount = InkSurface.GetSelectedStrokes().Count;
        var objectCount = _selectedPageObjectIds.Count;
        StatusText.Text = (inkCount, objectCount) switch
        {
            (0, 0) => $"套索范围内没有内容 · {MixedSelectionFilterName(_mixedSelectionFilter)}",
            (> 0, 0) => $"已选择 {inkCount} 条笔迹",
            (0, > 0) => $"已选择 {objectCount} 个对象",
            _ => $"已选择 {inkCount} 条笔迹和 {objectCount} 个对象"
        };
    }
}
