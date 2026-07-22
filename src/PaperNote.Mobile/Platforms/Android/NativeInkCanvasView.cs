using Android.Content;
using Android.Graphics;
using Android.Views;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Mobile.Controls;
using Color = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;
using Path = Android.Graphics.Path;
using RectF = Android.Graphics.RectF;
using View = Android.Views.View;

namespace PaperNote.Mobile.Platforms.Android;

public sealed class NativeInkCanvasView : View
{
    private const float PageWidth = 840f;
    private const float PageHeight = 1188f;
    private readonly Paint _paint = new(PaintFlags.AntiAlias | PaintFlags.Dither);
    private readonly Stack<CanvasSnapshot> _undo = new();
    private readonly Stack<CanvasSnapshot> _redo = new();
    private readonly InkSpatialIndex _inkSpatialIndex = new();
    private PaperInkDocument _document = new();
    private NotebookPage? _page;
    private InkCanvasTool _tool;
    private string _inkColor = "#1D2530";
    private double _inkWidth = 3.2;
    private double _inkOpacity = 1;
    private bool _fingerDrawing;
    private InkEraserMode _eraserMode = InkEraserMode.Partial;
    private bool _smoothingEnabled = true;
    private PaperInkStroke? _activeStroke;
    private bool _eraseUndoPushed;
    private bool _panning;
    private float _lastX;
    private float _lastY;
    private float _zoom = 1f;
    private float _fitScale = 1f;
    private float _offsetX;
    private float _offsetY;
    private float _lastPinchDistance;
    private float _lastPinchFocusX;
    private float _lastPinchFocusY;
    private Bitmap? _backgroundBitmap;
    private string _loadedBackground = string.Empty;
    private Guid? _selectedObjectId;
    private readonly HashSet<Guid> _selectedObjectIds = [];
    private bool _lassoSelecting;
    private float _lassoStartX;
    private float _lassoStartY;
    private float _lassoCurrentX;
    private float _lassoCurrentY;
    private bool _objectDragging;
    private bool _objectResizing;
    private bool _objectChanged;
    private bool _objectUndoPushed;
    private float _objectStartX;
    private float _objectStartY;
    private PageObjectBounds? _objectStartBounds;

    public NativeInkCanvasView(Context context) : base(context)
    {
        SetBackgroundColor(Color.Rgb(231, 234, 241));
        Focusable = true;
    }

    public event EventHandler? InkChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler? SelectionChanged;
    public Guid? SelectedObjectId => _selectedObjectId;
    public int SelectedObjectCount => _selectedObjectIds.Count;
    public IReadOnlyCollection<Guid> SelectedObjectIds => _selectedObjectIds.ToArray();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Apply(InkCanvasView view)
    {
        if (!ReferenceEquals(_document, view.Document))
        {
            _document = view.Document ?? new PaperInkDocument();
            _inkSpatialIndex.Rebuild(_document);
            _undo.Clear();
            _redo.Clear();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        if (!ReferenceEquals(_page, view.Page))
        {
            _page = view.Page;
            SetSelectedObject(null);
        }
        _tool = view.Tool;
        if (_tool != InkCanvasTool.Select && _selectedObjectId.HasValue) SetSelectedObject(null);
        _inkColor = view.InkColor;
        _inkWidth = view.InkWidth;
        _inkOpacity = view.InkOpacity;
        _fingerDrawing = view.FingerDrawingEnabled;
        _eraserMode = view.EraserMode;
        _smoothingEnabled = view.SmoothingEnabled;
        LoadBackgroundIfNeeded();
        Invalidate();
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        if (w > 0 && h > 0 && (oldw == 0 || oldh == 0)) ResetViewport();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        canvas.DrawColor(Color.Rgb(231, 234, 241));
        canvas.Save();
        canvas.Translate(_offsetX, _offsetY);
        canvas.Scale(_fitScale * _zoom, _fitScale * _zoom);
        DrawPage(canvas);
        canvas.Restore();
    }

    private void DrawPage(Canvas canvas)
    {
        var visibleStrokes = GetVisibleStrokes();
        AndroidPageRenderer.DrawPage(canvas, _page, _document, _paint, _backgroundBitmap, visibleStrokes);
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = 1f;
        _paint.Color = Color.Argb(30, 20, 30, 50);
        canvas.DrawRect(0, 0, PageWidth, PageHeight, _paint);
        DrawSelection(canvas);
    }

    private IReadOnlyList<PaperInkStroke> GetVisibleStrokes()
    {
        var scale = _fitScale * _zoom;
        if (scale <= 0 || Width <= 0 || Height <= 0) return _document.Strokes;
        const double overscan = 32;
        var left = (0 - _offsetX) / scale - overscan;
        var top = (0 - _offsetY) / scale - overscan;
        var right = (Width - _offsetX) / scale + overscan;
        var bottom = (Height - _offsetY) / scale + overscan;
        var indexed = _inkSpatialIndex.Query(left, top, right - left, bottom - top);
        if (_activeStroke is null || indexed.Contains(_activeStroke, ReferenceEqualityComparer.Instance)) return indexed;
        var result = new List<PaperInkStroke>(indexed.Count + 1);
        result.AddRange(indexed);
        result.Add(_activeStroke);
        return result;
    }

    private void DrawTemplate(Canvas canvas, string? template)
    {
        var kind = template ?? "Dotted";
        if (string.Equals(kind, "Blank", StringComparison.OrdinalIgnoreCase)) return;
        _paint.Color = Color.Argb(70, 125, 145, 170);
        _paint.StrokeWidth = 1f;
        _paint.SetStyle(Paint.Style.Stroke);
        if (string.Equals(kind, "Dotted", StringComparison.OrdinalIgnoreCase))
        {
            _paint.SetStyle(Paint.Style.Fill);
            for (var y = 36f; y < PageHeight; y += 36f)
                for (var x = 36f; x < PageWidth; x += 36f)
                    canvas.DrawCircle(x, y, 1.25f, _paint);
            return;
        }
        var step = string.Equals(kind, "Grid", StringComparison.OrdinalIgnoreCase) ? 36f : 42f;
        for (var y = step; y < PageHeight; y += step) canvas.DrawLine(0, y, PageWidth, y, _paint);
        if (string.Equals(kind, "Grid", StringComparison.OrdinalIgnoreCase))
            for (var x = step; x < PageWidth; x += step) canvas.DrawLine(x, 0, x, PageHeight, _paint);
    }

    private void DrawStrokes(Canvas canvas)
    {
        foreach (var stroke in _document.Strokes)
        {
            if (stroke.Points.Count == 0) continue;
            _paint.SetStyle(Paint.Style.Stroke);
            _paint.StrokeCap = Paint.Cap.Round;
            _paint.StrokeJoin = Paint.Join.Round;
            _paint.Color = ParseColor(stroke.Color, Color.Rgb(29, 37, 48));
            _paint.Alpha = (int)(255 * Math.Clamp(stroke.Opacity, 0.01, 1));
            if (stroke.Points.Count == 1)
            {
                var p = stroke.Points[0];
                _paint.SetStyle(Paint.Style.Fill);
                canvas.DrawCircle((float)p.X, (float)p.Y, (float)(EffectiveWidth(stroke, p) / 2), _paint);
                continue;
            }
            for (var i = 1; i < stroke.Points.Count; i++)
            {
                var a = stroke.Points[i - 1];
                var b = stroke.Points[i];
                _paint.StrokeWidth = (float)((EffectiveWidth(stroke, a) + EffectiveWidth(stroke, b)) / 2);
                canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, _paint);
            }
        }
        _paint.Alpha = 255;
    }

    private void DrawObjects(Canvas canvas)
    {
        if (_page?.Objects is not { Count: > 0 }) return;
        foreach (var item in _page.Objects)
        {
            _paint.Alpha = (int)(255 * Math.Clamp(item.Opacity, 0.1, 1));
            if (item.Kind == "Text")
            {
                _paint.SetStyle(Paint.Style.Fill);
                _paint.TextSize = (float)item.FontSize;
                _paint.Color = ParseColor(item.StrokeColor, Color.DarkGray);
                canvas.DrawText(item.Text ?? string.Empty, (float)item.X, (float)(item.Y + item.FontSize), _paint);
            }
            else if (item.Kind == "Image" && TryDecodeBitmap(item.ImageData, out var image))
            {
                using (image) canvas.DrawBitmap(image, null, new RectF((float)item.X, (float)item.Y, (float)(item.X + item.Width), (float)(item.Y + item.Height)), _paint);
            }
            else
            {
                _paint.SetStyle(Paint.Style.Stroke);
                _paint.StrokeWidth = (float)item.StrokeThickness;
                _paint.Color = ParseColor(item.StrokeColor, Color.Rgb(49, 87, 213));
                var rect = new RectF((float)item.X, (float)item.Y, (float)(item.X + item.Width), (float)(item.Y + item.Height));
                if (item.ShapeKind == "Ellipse") canvas.DrawOval(rect, _paint); else canvas.DrawRect(rect, _paint);
            }
        }
        _paint.Alpha = 255;
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;
        Parent?.RequestDisallowInterceptTouchEvent(true);
        if (e.PointerCount >= 2) return HandlePinch(e);
        if (_tool == InkCanvasTool.Select) return HandleObjectSelection(e);

        var x = e.GetX();
        var y = e.GetY();
        var toolType = e.GetToolType(0);
        var stylus = toolType is MotionEventToolType.Stylus or MotionEventToolType.Eraser;
        var erasing = toolType == MotionEventToolType.Eraser || _tool == InkCanvasTool.Eraser;
        var shouldDraw = stylus || _fingerDrawing;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _lastX = x; _lastY = y;
                if (erasing && shouldDraw)
                {
                    _eraseUndoPushed = false;
                    EraseAt(x, y);
                }
                else if (shouldDraw && _tool != InkCanvasTool.Pan && IsInsidePage(x, y))
                {
                    PushUndo();
                    StartStroke(e, x, y);
                }
                else _panning = true;
                return true;
            case MotionEventActions.Move:
                if (_activeStroke is not null) AddMotionPoints(e);
                else if (erasing && shouldDraw) EraseAt(x, y);
                else if (_panning)
                {
                    _offsetX += x - _lastX;
                    _offsetY += y - _lastY;
                    _lastX = x; _lastY = y;
                    Invalidate();
                }
                return true;
            case MotionEventActions.Up:
                if (_activeStroke is not null)
                {
                    AddPoint(e, x, y, e.Pressure);
                    if (_smoothingEnabled) InkEditingService.SmoothStroke(_activeStroke, .35);
                    var completedStroke = _activeStroke;
                    _activeStroke = null;
                    _inkSpatialIndex.Update(_document, null, [completedStroke]);
                    InkChanged?.Invoke(this, EventArgs.Empty);
                    HistoryChanged?.Invoke(this, EventArgs.Empty);
                }
                _eraseUndoPushed = false;
                _panning = false;
                _lastPinchDistance = 0;
                return true;
            case MotionEventActions.Cancel:
                CancelActiveStroke();
                _eraseUndoPushed = false;
                _panning = false;
                _lastPinchDistance = 0;
                return true;
        }
        return true;
    }

    private bool HandlePinch(MotionEvent e)
    {
        CancelActiveStroke();
        _eraseUndoPushed = false;
        _panning = false;
        var dx = e.GetX(1) - e.GetX(0);
        var dy = e.GetY(1) - e.GetY(0);
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        var focusX = (e.GetX(0) + e.GetX(1)) / 2f;
        var focusY = (e.GetY(0) + e.GetY(1)) / 2f;
        if (_lastPinchDistance > 0 && e.ActionMasked == MotionEventActions.Move)
        {
            var oldScale = _fitScale * _zoom;
            var nextZoom = Math.Clamp(_zoom * distance / _lastPinchDistance, 0.75f, 5f);
            var nextScale = _fitScale * nextZoom;
            var pageX = (focusX - _offsetX) / oldScale;
            var pageY = (focusY - _offsetY) / oldScale;
            _offsetX = focusX - pageX * nextScale + (focusX - _lastPinchFocusX);
            _offsetY = focusY - pageY * nextScale + (focusY - _lastPinchFocusY);
            _zoom = nextZoom;
            Invalidate();
        }
        _lastPinchDistance = distance;
        _lastPinchFocusX = focusX;
        _lastPinchFocusY = focusY;
        return true;
    }

    private bool IsInsidePage(float screenX, float screenY)
    {
        var scale = _fitScale * _zoom;
        if (scale <= 0) return false;
        var x = (screenX - _offsetX) / scale;
        var y = (screenY - _offsetY) / scale;
        return x is >= 0 and <= PageWidth && y is >= 0 and <= PageHeight;
    }

    private void StartStroke(MotionEvent e, float x, float y)
    {
        var highlighter = _tool == InkCanvasTool.Highlighter;
        _activeStroke = new PaperInkStroke
        {
            Tool = highlighter ? PaperInkTool.Highlighter : PaperInkTool.Pen,
            Color = _inkColor,
            Width = highlighter ? Math.Max(10, _inkWidth) : _inkWidth,
            Opacity = highlighter ? Math.Min(_inkOpacity, 0.35) : _inkOpacity,
            PressureEnabled = true,
            LayerId = _page is null ? null : PageLayerService.EnsureDefault(_page).Id
        };
        _document.Strokes.Add(_activeStroke);
        AddPoint(e, x, y, e.Pressure);
        Invalidate();
    }

    private void AddMotionPoints(MotionEvent e)
    {
        for (var h = 0; h < e.HistorySize; h++)
            AddPoint(e, e.GetHistoricalX(0, h), e.GetHistoricalY(0, h), e.GetHistoricalPressure(0, h));
        AddPoint(e, e.GetX(), e.GetY(), e.Pressure);
        Invalidate();
    }

    private void AddPoint(MotionEvent e, float screenX, float screenY, float pressure)
    {
        if (_activeStroke is null) return;
        var scale = _fitScale * _zoom;
        var x = (screenX - _offsetX) / scale;
        var y = (screenY - _offsetY) / scale;
        if (x is < 0 or > PageWidth || y is < 0 or > PageHeight) return;
        var tilt = e.GetAxisValue(Axis.Tilt);
        var orientation = e.GetAxisValue(Axis.Orientation);
        _activeStroke.Points.Add(new PaperInkPoint
        {
            X = x,
            Y = y,
            Pressure = Math.Clamp(pressure <= 0 ? 0.5 : pressure, 0, 1),
            TiltX = Math.Sin(orientation) * tilt,
            TiltY = Math.Cos(orientation) * tilt,
            TimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    private void EraseAt(float screenX, float screenY)
    {
        var scale = _fitScale * _zoom;
        var x = (screenX - _offsetX) / scale;
        var y = (screenY - _offsetY) / scale;
        var radius = 18 / Math.Max(scale, 0.01f);
        var matches = _inkSpatialIndex.QueryCircle(x, y, radius)
            .Where(stroke => StrokeTouchesCircle(stroke, x, y, radius))
            .ToArray();
        if (matches.Length == 0) return;
        if (!_eraseUndoPushed)
        {
            PushUndo();
            _eraseUndoPushed = true;
        }

        var changed = 0;
        if (_eraserMode == InkEraserMode.Stroke)
        {
            changed = RemoveWholeStrokes(matches);
            if (changed > 0) _inkSpatialIndex.Update(_document, matches, null);
        }
        else
        {
            var result = InkEditingService.ErasePartialDetailed(_document, x, y, radius, matches);
            changed = result.ChangedCount;
            if (changed > 0) _inkSpatialIndex.Update(_document, result.RemovedStrokes, result.AddedStrokes);
        }
        if (changed == 0) return;
        Invalidate();
        InkChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }


    private static bool StrokeTouchesCircle(PaperInkStroke stroke, double x, double y, double radius)
    {
        var radiusSquared = radius * radius;
        if (stroke.Points.Count == 1)
        {
            var point = stroke.Points[0];
            return DistanceSquared(point.X, point.Y, x, y) <= radiusSquared;
        }
        for (var index = 1; index < stroke.Points.Count; index++)
        {
            var start = stroke.Points[index - 1];
            var end = stroke.Points[index];
            if (DistanceToSegmentSquared(x, y, start.X, start.Y, end.X, end.Y) <= radiusSquared) return true;
        }
        return false;
    }

    private static double DistanceToSegmentSquared(double x, double y, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon) return DistanceSquared(x, y, x1, y1);
        var projection = Math.Clamp(((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy), 0, 1);
        return DistanceSquared(x, y, x1 + projection * dx, y1 + projection * dy);
    }

    private static double DistanceSquared(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return dx * dx + dy * dy;
    }

    private int RemoveWholeStrokes(IEnumerable<PaperInkStroke> strokes)
    {
        var removed = 0;
        foreach (var stroke in strokes.ToArray()) if (_document.Strokes.Remove(stroke)) removed++;
        return removed;
    }

    private void CancelActiveStroke()
    {
        if (_activeStroke is null) return;
        _document.Strokes.Remove(_activeStroke);
        _activeStroke = null;
        if (_undo.Count > 0) _undo.Pop();
        Invalidate();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PushUndo()
    {
        _undo.Push(CaptureSnapshot());
        while (_undo.Count > 100)
        {
            var keep = _undo.Reverse().TakeLast(100).ToArray();
            _undo.Clear();
            foreach (var item in keep) _undo.Push(item);
        }
        _redo.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(CaptureSnapshot());
        ReplaceSnapshot(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(CaptureSnapshot());
        ReplaceSnapshot(_redo.Pop());
    }

    public void ClearInk()
    {
        if (_document.IsEmpty) return;
        PushUndo();
        _document.Strokes.Clear();
        _inkSpatialIndex.Rebuild(_document);
        Invalidate();
        InkChanged?.Invoke(this, EventArgs.Empty);
    }

    private CanvasSnapshot CaptureSnapshot()
        => new(_document.Clone(), _page?.Objects.Select(item => item.Clone()).ToList() ?? []);

    private void ReplaceSnapshot(CanvasSnapshot snapshot)
    {
        _document.Version = snapshot.Ink.Version;
        _document.Strokes = snapshot.Ink.Strokes.Select(stroke => stroke.Clone()).ToList();
        _inkSpatialIndex.Rebuild(_document);
        if (_page is not null) _page.Objects = snapshot.Objects.Select(item => item.Clone()).ToList();
        if (_page is not null) SetSelection(_selectedObjectIds.Where(id => _page.Objects.Any(item => item.Id == id)));
        Invalidate();
        InkChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DrawSelection(Canvas canvas)
    {
        if (_tool != InkCanvasTool.Select || _page is null) return;
        if (_lassoSelecting)
        {
            var lasso = NormalizeBounds(_lassoStartX, _lassoStartY, _lassoCurrentX, _lassoCurrentY);
            _paint.SetStyle(Paint.Style.Fill);
            _paint.Color = Color.Argb(28, 49, 87, 213);
            canvas.DrawRect((float)lasso.X, (float)lasso.Y, (float)lasso.Right, (float)lasso.Bottom, _paint);
            _paint.SetStyle(Paint.Style.Stroke);
            _paint.StrokeWidth = 2f;
            _paint.Color = Color.Rgb(49, 87, 213);
            using var lassoDash = new DashPathEffect([10, 7], 0);
            _paint.SetPathEffect(lassoDash);
            canvas.DrawRect((float)lasso.X, (float)lasso.Y, (float)lasso.Right, (float)lasso.Bottom, _paint);
            _paint.SetPathEffect(null);
        }
        if (_selectedObjectIds.Count == 0) return;
        var bounds = PageObjectEditingService.GetBounds(_page, _selectedObjectIds);
        if (bounds is null) return;
        var box = bounds.Value;
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = 3f;
        _paint.Color = Color.Rgb(49, 87, 213);
        using var dash = new DashPathEffect([12, 8], 0);
        _paint.SetPathEffect(dash);
        canvas.DrawRect((float)box.X, (float)box.Y, (float)box.Right, (float)box.Bottom, _paint);
        _paint.SetPathEffect(null);
        _paint.SetStyle(Paint.Style.Fill);
        _paint.Color = Color.White;
        canvas.DrawCircle((float)box.Right, (float)box.Bottom, 12, _paint);
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = 3f;
        _paint.Color = Color.Rgb(49, 87, 213);
        canvas.DrawCircle((float)box.Right, (float)box.Bottom, 12, _paint);
        if (_selectedObjectIds.Any(id => _page.Objects.FirstOrDefault(item => item.Id == id)?.IsLocked == true))
        {
            _paint.SetStyle(Paint.Style.Fill);
            _paint.Color = Color.Rgb(239, 143, 40);
            canvas.DrawCircle((float)box.X, (float)box.Y, 13, _paint);
            _paint.Color = Color.White;
            _paint.TextSize = 15;
            canvas.DrawText("L", (float)box.X - 4.5f, (float)box.Y + 5f, _paint);
        }
    }

    private bool HandleObjectSelection(MotionEvent e)
    {
        if (_page is null) return true;
        var scale = Math.Max(_fitScale * _zoom, .01f);
        var pageX = (e.GetX() - _offsetX) / scale;
        var pageY = (e.GetY() - _offsetY) / scale;
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            {
                _objectDragging = false;
                _objectResizing = false;
                _objectChanged = false;
                _objectUndoPushed = false;
                _lassoSelecting = false;
                var selectedBounds = PageObjectEditingService.GetBounds(_page, _selectedObjectIds);
                var handleTolerance = 28 / scale;
                if (selectedBounds is PageObjectBounds current &&
                    Distance(pageX, pageY, current.Right, current.Bottom) <= handleTolerance &&
                    SelectionCanTransform())
                {
                    _objectResizing = true;
                    _objectStartBounds = current;
                    _objectStartX = pageX;
                    _objectStartY = pageY;
                    PushUndo();
                    _objectUndoPushed = true;
                    return true;
                }

                var hit = PageObjectEditingService.HitTest(_page, pageX, pageY, 10 / scale);
                if (hit is not null)
                {
                    SetSelection(PageObjectEditingService.ExpandSelection(_page, [hit.Id]));
                    if (!hit.IsLocked && !PageLayerService.IsContentLocked(_page, hit.LayerId))
                    {
                        _objectDragging = true;
                        _objectStartX = pageX;
                        _objectStartY = pageY;
                        PushUndo();
                        _objectUndoPushed = true;
                    }
                }
                else
                {
                    SetSelection([]);
                    if (pageX is >= 0 and <= PageWidth && pageY is >= 0 and <= PageHeight)
                    {
                        _lassoSelecting = true;
                        _lassoStartX = _lassoCurrentX = pageX;
                        _lassoStartY = _lassoCurrentY = pageY;
                    }
                }
                return true;
            }
            case MotionEventActions.Move:
                if (_lassoSelecting)
                {
                    _lassoCurrentX = Math.Clamp(pageX, 0, PageWidth);
                    _lassoCurrentY = Math.Clamp(pageY, 0, PageHeight);
                    Invalidate();
                }
                else if (_objectDragging)
                {
                    var dx = pageX - _objectStartX;
                    var dy = pageY - _objectStartY;
                    if (PageObjectEditingService.Move(_page, _selectedObjectIds, dx, dy))
                    {
                        _objectStartX = pageX;
                        _objectStartY = pageY;
                        _objectChanged = true;
                        Invalidate();
                    }
                }
                else if (_objectResizing && _objectStartBounds is PageObjectBounds resizeStart)
                {
                    if (PageObjectEditingService.Resize(_page, _selectedObjectIds, resizeStart.Width + pageX - _objectStartX, resizeStart.Height + pageY - _objectStartY))
                    {
                        _objectChanged = true;
                        Invalidate();
                    }
                }
                return true;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_lassoSelecting)
                {
                    _lassoCurrentX = Math.Clamp(pageX, 0, PageWidth);
                    _lassoCurrentY = Math.Clamp(pageY, 0, PageHeight);
                    if (e.ActionMasked == MotionEventActions.Up) SelectByLasso();
                }
                _lassoSelecting = false;
                _objectDragging = false;
                _objectResizing = false;
                _objectStartBounds = null;
                if (_objectChanged) NotifyContentChanged();
                else if (_objectUndoPushed && _undo.Count > 0) { _undo.Pop(); HistoryChanged?.Invoke(this, EventArgs.Empty); }
                _objectChanged = false;
                _objectUndoPushed = false;
                Invalidate();
                return true;
            default:
                return true;
        }
    }

    private void SelectByLasso()
    {
        if (_page is null) return;
        var box = NormalizeBounds(_lassoStartX, _lassoStartY, _lassoCurrentX, _lassoCurrentY);
        if (box.Width < 6 && box.Height < 6) { SetSelection([]); return; }
        var ids = _page.Objects
            .Where(item => !item.IsHidden && PageLayerService.IsContentVisible(_page, item.LayerId))
            .Where(item => item.X <= box.Right && item.X + item.Width >= box.X && item.Y <= box.Bottom && item.Y + item.Height >= box.Y)
            .Select(item => item.Id)
            .ToArray();
        SetSelection(PageObjectEditingService.ExpandSelection(_page, ids));
    }

    private bool SelectionCanTransform()
        => _page is not null && _selectedObjectIds.Count > 0 && _selectedObjectIds
            .Select(id => _page.Objects.FirstOrDefault(item => item.Id == id))
            .Where(item => item is not null)
            .All(item => item!.IsLocked == false && !PageLayerService.IsContentLocked(_page, item.LayerId));

    private static PageObjectBounds NormalizeBounds(double x1, double y1, double x2, double y2)
        => new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public void SelectObject(Guid? objectId)
    {
        if (_page is null || objectId is not Guid id || _page.Objects.All(item => item.Id != id)) SetSelection([]);
        else SetSelection(PageObjectEditingService.ExpandSelection(_page, [id]));
    }

    private void SetSelectedObject(Guid? objectId) => SelectObject(objectId);

    private void SetSelection(IEnumerable<Guid> objectIds)
    {
        var valid = _page is null ? [] : objectIds.Where(id => _page.Objects.Any(item => item.Id == id)).Distinct().ToArray();
        if (_selectedObjectIds.SetEquals(valid)) return;
        _selectedObjectIds.Clear();
        foreach (var id in valid) _selectedObjectIds.Add(id);
        _selectedObjectId = valid.FirstOrDefault();
        if (_selectedObjectId == Guid.Empty) _selectedObjectId = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void NotifyContentChanged()
    {
        InkChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private bool MutateSelection(Func<NotebookPage, IReadOnlyCollection<Guid>, bool> mutation)
    {
        if (_page is null || _selectedObjectIds.Count == 0) return false;
        var ids = _selectedObjectIds.ToArray();
        PushUndo();
        if (!mutation(_page, ids))
        {
            _undo.Pop();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }
        SetSelection(ids.Where(id => _page.Objects.Any(item => item.Id == id)));
        NotifyContentChanged();
        return true;
    }

    public void DuplicateSelection()
    {
        if (_page is null || _selectedObjectIds.Count == 0) return;
        PushUndo();
        var ids = PageObjectEditingService.Duplicate(_page, _selectedObjectIds);
        if (ids.Count == 0) { _undo.Pop(); HistoryChanged?.Invoke(this, EventArgs.Empty); return; }
        SetSelection(ids);
        NotifyContentChanged();
    }

    public void DeleteSelection()
    {
        if (MutateSelection((page, ids) => PageObjectEditingService.Delete(page, ids))) SetSelection([]);
    }

    public void RotateSelection(double degrees) => MutateSelection((page, ids) => PageObjectEditingService.Rotate(page, ids, degrees));
    public void BringSelectionToFront() => MutateSelection((page, ids) => PageObjectEditingService.BringToFront(page, ids));
    public void SendSelectionToBack() => MutateSelection((page, ids) => PageObjectEditingService.SendToBack(page, ids));
    public void GroupSelection()
    {
        if (_page is null || _selectedObjectIds.Count < 2) return;
        MutateSelection((page, ids) => PageObjectEditingService.Group(page, ids).HasValue);
    }
    public void UngroupSelection() => MutateSelection((page, ids) => PageObjectEditingService.Ungroup(page, ids));

    public void ToggleSelectionLock()
    {
        if (_page is null || _selectedObjectIds.Count == 0) return;
        var selected = _page.Objects.Where(item => _selectedObjectIds.Contains(item.Id)).ToArray();
        var shouldLock = selected.Any(item => !item.IsLocked);
        MutateSelection((page, ids) => PageObjectEditingService.SetLocked(page, ids, shouldLock));
    }

    public void UpdateSelectedText(string text)
    {
        if (_page is null || _selectedObjectIds.Count != 1) return;
        MutateSelection((page, ids) =>
        {
            var selected = page.Objects.FirstOrDefault(item => ids.Contains(item.Id));
            if (selected is null || selected.IsLocked || selected.Kind != "Text") return false;
            selected.Text = text;
            page.ModifiedAt = DateTimeOffset.Now;
            return true;
        });
    }

    public void UpdateSelectionStyle(string strokeColor, double opacity)
        => MutateSelection((page, ids) => PageObjectEditingService.UpdateStyle(page, ids, strokeColor: strokeColor, opacity: opacity));

    public void ResetViewport()
    {
        if (Width <= 0 || Height <= 0) return;
        _fitScale = Math.Min((float)Width / PageWidth, (float)Height / PageHeight) * 0.94f;
        _zoom = 1f;
        _offsetX = ((float)Width - PageWidth * _fitScale) / 2f;
        _offsetY = ((float)Height - PageHeight * _fitScale) / 2f;
        Invalidate();
    }

    private void LoadBackgroundIfNeeded()
    {
        var data = _page?.BackgroundImageData ?? string.Empty;
        if (string.Equals(data, _loadedBackground, StringComparison.Ordinal)) return;
        _backgroundBitmap?.Dispose();
        _backgroundBitmap = null;
        _loadedBackground = data;
        if (TryDecodeBitmap(data, out var bitmap)) _backgroundBitmap = bitmap;
    }

    public void DisposeResources()
    {
        _backgroundBitmap?.Dispose();
        _backgroundBitmap = null;
        _paint.Dispose();
    }

    private static bool TryDecodeBitmap(string? data, out Bitmap bitmap)
    {
        bitmap = null!;
        if (string.IsNullOrWhiteSpace(data)) return false;
        try
        {
            var comma = data.IndexOf(',');
            var payload = data.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0 ? data[(comma + 1)..] : data;
            var bytes = Convert.FromBase64String(payload);
            bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length)!;
            return bitmap is not null;
        }
        catch { return false; }
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        try { return Color.ParseColor(value ?? string.Empty); }
        catch { return fallback; }
    }

    private static double EffectiveWidth(PaperInkStroke stroke, PaperInkPoint point)
    {
        if (!stroke.PressureEnabled || stroke.Tool == PaperInkTool.Highlighter) return stroke.Width;
        return stroke.Width * (0.35 + Math.Clamp(point.Pressure, 0, 1) * 0.9);
    }

    private static double Distance(double ax, double ay, double bx, double by)
        => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    private sealed record CanvasSnapshot(PaperInkDocument Ink, List<PageObject> Objects);
}
