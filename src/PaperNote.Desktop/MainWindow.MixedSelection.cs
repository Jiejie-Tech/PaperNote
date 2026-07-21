using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using PaperNote.Core.Models;
using PaperNote.Desktop.Services;
using PaperNote.Core.Services;

namespace PaperNote.Desktop;

public partial class MainWindow
{
    private readonly List<Point> _mixedLassoPoints = [];
    private bool _isMixedLassoActive;
    private bool _mixedLassoUsesStylus;
    private ModifierKeys _mixedLassoModifiers;

    private bool SelectAllInkAndPageObjects()
    {
        if (_isReadOnly || _currentPage is null || _activeTool != "Select") return false;

        InkSurface.Select(InkSurface.Strokes);
        _selectedPageObjectIds.Clear();
        foreach (var pageObject in _currentPage.Objects) _selectedPageObjectIds.Add(pageObject.Id);

        SetPrimarySelectionFromCurrentIds();
        UpdatePageObjectSelectionVisuals();
        UpdateMixedSelectionStatus();
        return InkSurface.Strokes.Count > 0 || _selectedPageObjectIds.Count > 0;
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
        var closedPoints = lassoPoints.ToList();
        if (closedPoints[0] != closedPoints[^1]) closedPoints.Add(closedPoints[0]);
        var hitStrokes = InkSurface.Strokes.HitTest(closedPoints, 45);

        if (additive)
        {
            var combined = new StrokeCollection(InkSurface.GetSelectedStrokes());
            foreach (var stroke in hitStrokes)
            {
                if (!combined.Contains(stroke)) combined.Add(stroke);
            }
            InkSurface.Select(combined);
        }
        else
        {
            InkSurface.Select(hitStrokes);
            _selectedPageObjectIds.Clear();
        }

        foreach (var pageObject in _currentPage.Objects.Where(item => IsObjectHitByLasso(item, closedPoints)))
        {
            foreach (var member in GetSelectionUnit(pageObject)) _selectedPageObjectIds.Add(member.Id);
        }

        SetPrimarySelectionFromCurrentIds();
        UpdatePageObjectSelectionVisuals();
        UpdateMixedSelectionStatus();
    }

    private static bool IsObjectHitByLasso(PageObject pageObject, IReadOnlyList<Point> polygon)
    {
        var rect = new Rect(pageObject.X, pageObject.Y, pageObject.Width, pageObject.Height);
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        if (IsPointInPolygon(center, polygon)) return true;
        if (IsPointInPolygon(rect.TopLeft, polygon) || IsPointInPolygon(rect.TopRight, polygon) ||
            IsPointInPolygon(rect.BottomLeft, polygon) || IsPointInPolygon(rect.BottomRight, polygon)) return true;
        return polygon.Any(rect.Contains);
    }

    private static bool IsPointInPolygon(Point point, IReadOnlyList<Point> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = i == 0 ? polygon.Count - 1 : i - 1;
            var pi = polygon[i];
            var pj = polygon[j];
            if ((pi.Y > point.Y) == (pj.Y > point.Y)) continue;
            var crossingX = (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X;
            if (point.X < crossingX) inside = !inside;
        }
        return inside;
    }

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

    private void ClearMixedSelection()
    {
        InkSurface.Select(new StrokeCollection());
        ClearPageObjectSelection();
        StatusText.Text = "已清除选择";
    }

    private bool DeleteMixedSelection()
    {
        if (_isReadOnly || _currentPage is null) return false;

        var selectedStrokes = InkSurface.GetSelectedStrokes();
        var deletingObjectIds = SelectedPageObjects()
            .Where(item => !item.IsLocked)
            .Select(item => item.Id)
            .ToHashSet();
        if (selectedStrokes.Count == 0 && deletingObjectIds.Count == 0) return false;

        var selectedStrokeCount = selectedStrokes.Count;
        if (selectedStrokeCount > 0)
        {
            _history.Record(InkSurface.Strokes);
            InkSurface.Strokes.Remove(selectedStrokes);
            UpdateHistoryButtons();
        }

        if (deletingObjectIds.Count > 0)
        {
            _currentPage.Objects.RemoveAll(item => deletingObjectIds.Contains(item.Id));
            foreach (var id in deletingObjectIds)
            {
                _selectedPageObjectIds.Remove(id);
                if (_pageObjectContainers.Remove(id, out var removedContainer)) ObjectLayer.Children.Remove(removedContainer);
            }
            _selectedPageObjectIds.IntersectWith(_currentPage.Objects.Select(item => item.Id));
            SetPrimarySelectionFromCurrentIds();
            UpdatePageObjectSelectionVisuals();
        }

        _currentPage.ModifiedAt = DateTimeOffset.Now;
        UpdateCurrentPageThumbnail();
        MarkDirty();

        var lockedSelectedCount = SelectedPageObjects().Count(item => item.IsLocked);
        var deletedParts = new List<string>();
        if (selectedStrokeCount > 0) deletedParts.Add($"{selectedStrokeCount} 条笔迹");
        if (deletingObjectIds.Count > 0) deletedParts.Add($"{deletingObjectIds.Count} 个对象");
        StatusText.Text = $"已删除 {string.Join("和", deletedParts)}" +
                          (lockedSelectedCount > 0 ? $"，保留 {lockedSelectedCount} 个锁定对象" : string.Empty);
        return true;
    }

    private void UpdateMixedSelectionStatus()
    {
        var inkCount = InkSurface.GetSelectedStrokes().Count;
        var objectCount = _selectedPageObjectIds.Count;
        StatusText.Text = inkCount == 0 && objectCount == 0
            ? "套索范围内没有内容"
            : $"已选择 {inkCount} 条笔迹和 {objectCount} 个对象";
    }
}
