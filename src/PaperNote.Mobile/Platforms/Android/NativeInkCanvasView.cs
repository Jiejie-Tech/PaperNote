using Android.Content;
using Android.Graphics;
using Android.Views;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;
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
    private readonly Stack<PaperInkDocument> _undo = new();
    private readonly Stack<PaperInkDocument> _redo = new();
    private PaperInkDocument _document = new();
    private NotebookPage? _page;
    private InkCanvasTool _tool;
    private string _inkColor = "#1D2530";
    private double _inkWidth = 3.2;
    private bool _fingerDrawing;
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

    public NativeInkCanvasView(Context context) : base(context)
    {
        SetBackgroundColor(Color.Rgb(231, 234, 241));
        Focusable = true;
    }

    public event EventHandler? InkChanged;
    public event EventHandler? HistoryChanged;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Apply(InkCanvasView view)
    {
        if (!ReferenceEquals(_document, view.Document))
        {
            _document = view.Document ?? new PaperInkDocument();
            _undo.Clear();
            _redo.Clear();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        _page = view.Page;
        _tool = view.Tool;
        _inkColor = view.InkColor;
        _inkWidth = view.InkWidth;
        _fingerDrawing = view.FingerDrawingEnabled;
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
        AndroidPageRenderer.DrawPage(canvas, _page, _document, _paint, _backgroundBitmap);
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = 1f;
        _paint.Color = Color.Argb(30, 20, 30, 50);
        canvas.DrawRect(0, 0, PageWidth, PageHeight, _paint);
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
                    _activeStroke = null;
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
            Opacity = highlighter ? 0.35 : 1,
            PressureEnabled = true
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
        var matches = _document.Strokes.Where(stroke => stroke.Points.Any(point => Distance(point.X, point.Y, x, y) <= radius)).ToArray();
        if (matches.Length == 0) return;
        if (!_eraseUndoPushed)
        {
            PushUndo();
            _eraseUndoPushed = true;
        }
        foreach (var stroke in matches) _document.Strokes.Remove(stroke);
        Invalidate();
        InkChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
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
        _undo.Push(_document.Clone());
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
        _redo.Push(_document.Clone());
        ReplaceDocument(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(_document.Clone());
        ReplaceDocument(_redo.Pop());
    }

    public void ClearInk()
    {
        if (_document.IsEmpty) return;
        PushUndo();
        _document.Strokes.Clear();
        Invalidate();
        InkChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceDocument(PaperInkDocument snapshot)
    {
        _document.Version = snapshot.Version;
        _document.Strokes = snapshot.Strokes.Select(stroke => stroke.Clone()).ToList();
        Invalidate();
        InkChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

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
}
