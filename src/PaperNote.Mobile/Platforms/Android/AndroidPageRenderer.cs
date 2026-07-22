using Android.Graphics;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;
using PaperNote.Core.Services;
using AColor = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;
using Path = Android.Graphics.Path;
using Rect = Android.Graphics.Rect;
using RectF = Android.Graphics.RectF;

namespace PaperNote.Mobile.Platforms.Android;

internal static class AndroidPageRenderer
{
    public const float PageWidth = 840f;
    public const float PageHeight = 1188f;

    public static void DrawPage(Canvas canvas, NotebookPage? page, PaperInkDocument document, Paint paint, Bitmap? background)
    {
        paint.SetStyle(Paint.Style.Fill);
        paint.Color = ParseColor(page?.PaperColor, AColor.White);
        paint.Alpha = 255;
        canvas.DrawRect(0, 0, PageWidth, PageHeight, paint);
        DrawTemplate(canvas, page?.PaperTemplate, paint);
        if (background is not null && page is not null) DrawBackground(canvas, background, page, paint);
        DrawStrokes(canvas, page, document, paint);
        if (page is not null) DrawObjects(canvas, page, paint);
        paint.Alpha = 255;
    }

    private static void DrawTemplate(Canvas canvas, string? template, Paint paint)
    {
        var kind = template ?? PaperPageDefaults.Template;
        if (string.Equals(kind, "Blank", StringComparison.OrdinalIgnoreCase)) return;
        paint.Color = AColor.Argb(70, 125, 145, 170);
        paint.StrokeWidth = 1f;
        paint.SetStyle(Paint.Style.Stroke);
        if (string.Equals(kind, "Dotted", StringComparison.OrdinalIgnoreCase))
        {
            paint.SetStyle(Paint.Style.Fill);
            for (var y = 36f; y < PageHeight; y += 36f)
                for (var x = 36f; x < PageWidth; x += 36f)
                    canvas.DrawCircle(x, y, 1.25f, paint);
            return;
        }

        var step = string.Equals(kind, "Grid", StringComparison.OrdinalIgnoreCase) ? 36f : 42f;
        for (var y = step; y < PageHeight; y += step) canvas.DrawLine(0, y, PageWidth, y, paint);
        if (string.Equals(kind, "Grid", StringComparison.OrdinalIgnoreCase))
            for (var x = step; x < PageWidth; x += step) canvas.DrawLine(x, 0, x, PageHeight, paint);
    }

    private static void DrawBackground(Canvas canvas, Bitmap bitmap, NotebookPage page, Paint paint)
    {
        var left = Math.Clamp(page.BackgroundCropLeft, 0, 0.45);
        var top = Math.Clamp(page.BackgroundCropTop, 0, 0.45);
        var right = Math.Clamp(page.BackgroundCropRight, 0, 0.45);
        var bottom = Math.Clamp(page.BackgroundCropBottom, 0, 0.45);
        var x = Math.Clamp((int)Math.Round(bitmap.Width * left), 0, bitmap.Width - 1);
        var y = Math.Clamp((int)Math.Round(bitmap.Height * top), 0, bitmap.Height - 1);
        var width = Math.Max(1, bitmap.Width - x - (int)Math.Round(bitmap.Width * right));
        var height = Math.Max(1, bitmap.Height - y - (int)Math.Round(bitmap.Height * bottom));
        width = Math.Min(width, bitmap.Width - x);
        height = Math.Min(height, bitmap.Height - y);
        using var source = new Rect(x, y, x + width, y + height);
        var rotation = ((page.BackgroundRotation % 360) + 360) % 360;

        var save = canvas.Save();
        canvas.ClipRect(0, 0, PageWidth, PageHeight);
        switch (rotation)
        {
            case 90:
                canvas.Translate(PageWidth, 0);
                canvas.Rotate(90);
                canvas.DrawBitmap(bitmap, source, new RectF(0, 0, PageHeight, PageWidth), paint);
                break;
            case 180:
                canvas.Translate(PageWidth, PageHeight);
                canvas.Rotate(180);
                canvas.DrawBitmap(bitmap, source, new RectF(0, 0, PageWidth, PageHeight), paint);
                break;
            case 270:
                canvas.Translate(0, PageHeight);
                canvas.Rotate(270);
                canvas.DrawBitmap(bitmap, source, new RectF(0, 0, PageHeight, PageWidth), paint);
                break;
            default:
                canvas.DrawBitmap(bitmap, source, new RectF(0, 0, PageWidth, PageHeight), paint);
                break;
        }
        canvas.RestoreToCount(save);
    }

    private static void DrawStrokes(Canvas canvas, NotebookPage? page, PaperInkDocument document, Paint paint)
    {
        foreach (var stroke in document.Strokes)
        {
            if (stroke.Points.Count == 0 || !PageLayerService.IsContentVisible(page, stroke.LayerId)) continue;
            paint.SetStyle(Paint.Style.Stroke);
            paint.StrokeCap = Paint.Cap.Round;
            paint.StrokeJoin = Paint.Join.Round;
            paint.Color = ParseColor(stroke.Color, AColor.Rgb(29, 37, 48));
            paint.Alpha = (int)(255 * PageLayerService.GetEffectiveOpacity(page, stroke.LayerId, stroke.Opacity));
            if (stroke.Points.Count == 1)
            {
                var point = stroke.Points[0];
                paint.SetStyle(Paint.Style.Fill);
                canvas.DrawCircle((float)point.X, (float)point.Y, (float)(EffectiveWidth(stroke, point) / 2), paint);
                continue;
            }

            for (var i = 1; i < stroke.Points.Count; i++)
            {
                var a = stroke.Points[i - 1];
                var b = stroke.Points[i];
                paint.StrokeWidth = (float)((EffectiveWidth(stroke, a) + EffectiveWidth(stroke, b)) / 2);
                canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, paint);
            }
        }
        paint.Alpha = 255;
    }

    private static void DrawObjects(Canvas canvas, NotebookPage page, Paint paint)
    {
        foreach (var item in page.Objects)
        {
            if (item.IsHidden || !PageLayerService.IsContentVisible(page, item.LayerId)) continue;
            var left = (float)item.X;
            var top = (float)item.Y;
            var right = (float)(item.X + item.Width);
            var bottom = (float)(item.Y + item.Height);
            var rect = new RectF(left, top, right, bottom);
            var save = canvas.Save();
            if (Math.Abs(item.Rotation) > 0.01)
                canvas.Rotate((float)item.Rotation, rect.CenterX(), rect.CenterY());
            paint.Alpha = (int)(255 * PageLayerService.GetEffectiveOpacity(page, item.LayerId, item.Opacity));

            if (string.Equals(item.Kind, "Text", StringComparison.OrdinalIgnoreCase)) DrawText(canvas, item, rect, paint);
            else if (string.Equals(item.Kind, "Image", StringComparison.OrdinalIgnoreCase) && TryDecode(item.ImageData, out var image))
            {
                using (image) canvas.DrawBitmap(image, null, rect, paint);
            }
            else DrawShape(canvas, item, rect, paint);

            canvas.RestoreToCount(save);
        }
        paint.Alpha = 255;
    }

    private static void DrawText(Canvas canvas, PageObject item, RectF rect, Paint paint)
    {
        paint.SetStyle(Paint.Style.Fill);
        paint.TextSize = (float)Math.Clamp(item.FontSize, 10, 96);
        paint.Color = ParseColor(item.StrokeColor, AColor.DarkGray);
        var text = item.Text ?? string.Empty;
        var maxWidth = Math.Max(20f, rect.Width() - 18f);
        var lineHeight = paint.TextSize * 1.25f;
        var maxLines = Math.Max(1, (int)((rect.Height() - 18f) / lineHeight));
        var lines = WrapText(text, paint, maxWidth, maxLines);
        var y = rect.Top + 9f + paint.TextSize;
        foreach (var line in lines)
        {
            canvas.DrawText(line, rect.Left + 9f, y, paint);
            y += lineHeight;
        }
    }

    private static IReadOnlyList<string> WrapText(string text, Paint paint, float maxWidth, int maxLines)
    {
        var result = new List<string>();
        var current = string.Empty;
        foreach (var ch in text.Replace("\r", string.Empty))
        {
            if (ch == '\n')
            {
                result.Add(current);
                current = string.Empty;
            }
            else
            {
                var next = current + ch;
                if (current.Length > 0 && paint.MeasureText(next) > maxWidth)
                {
                    result.Add(current);
                    current = ch.ToString();
                }
                else current = next;
            }
            if (result.Count >= maxLines) break;
        }
        if (result.Count < maxLines && (current.Length > 0 || result.Count == 0)) result.Add(current);
        if (result.Count == maxLines && text.Length > result.Sum(line => line.Length))
        {
            var last = result[^1];
            while (last.Length > 0 && paint.MeasureText(last + "…") > maxWidth) last = last[..^1];
            result[^1] = last + "…";
        }
        return result;
    }

    private static void DrawShape(Canvas canvas, PageObject item, RectF rect, Paint paint)
    {
        var inset = 8f;
        var shape = new RectF(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
        paint.SetStyle(Paint.Style.Fill);
        paint.Color = ParseColor(item.FillColor, AColor.Argb(26, 57, 120, 246));
        DrawShapeGeometry(canvas, item.ShapeKind, shape, paint);
        paint.SetStyle(Paint.Style.Stroke);
        paint.StrokeWidth = (float)Math.Clamp(item.StrokeThickness, 1, 20);
        paint.Color = ParseColor(item.StrokeColor, AColor.Rgb(57, 120, 246));
        DrawShapeGeometry(canvas, item.ShapeKind, shape, paint);
    }

    private static void DrawShapeGeometry(Canvas canvas, string? shapeKind, RectF rect, Paint paint)
    {
        switch (shapeKind)
        {
            case "Ellipse":
                canvas.DrawOval(rect, paint);
                return;
            case "Line":
                if (paint.GetStyle() == Paint.Style.Stroke) canvas.DrawLine(rect.Left, rect.Bottom, rect.Right, rect.Top, paint);
                return;
            case "RoundedRectangle":
                canvas.DrawRoundRect(rect, 18, 18, paint);
                return;
            case "Triangle":
                using (var triangle = ClosedPath((rect.CenterX(), rect.Top), (rect.Right, rect.Bottom), (rect.Left, rect.Bottom)))
                    canvas.DrawPath(triangle, paint);
                return;
            case "Diamond":
                using (var diamond = ClosedPath((rect.CenterX(), rect.Top), (rect.Right, rect.CenterY()), (rect.CenterX(), rect.Bottom), (rect.Left, rect.CenterY())))
                    canvas.DrawPath(diamond, paint);
                return;
            case "Arrow":
                using (var arrow = ClosedPath(
                    (rect.Left, rect.Top + rect.Height() * .34f),
                    (rect.Left + rect.Width() * .62f, rect.Top + rect.Height() * .34f),
                    (rect.Left + rect.Width() * .62f, rect.Top),
                    (rect.Right, rect.CenterY()),
                    (rect.Left + rect.Width() * .62f, rect.Bottom),
                    (rect.Left + rect.Width() * .62f, rect.Top + rect.Height() * .66f),
                    (rect.Left, rect.Top + rect.Height() * .66f)))
                    canvas.DrawPath(arrow, paint);
                return;
            default:
                canvas.DrawRect(rect, paint);
                return;
        }
    }

    private static Path ClosedPath(params (float X, float Y)[] points)
    {
        var path = new Path();
        if (points.Length == 0) return path;
        path.MoveTo(points[0].X, points[0].Y);
        foreach (var point in points.Skip(1)) path.LineTo(point.X, point.Y);
        path.Close();
        return path;
    }

    private static bool TryDecode(string? data, out Bitmap bitmap)
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

    private static AColor ParseColor(string? value, AColor fallback)
    {
        try { return AColor.ParseColor(value ?? string.Empty); }
        catch { return fallback; }
    }

    private static double EffectiveWidth(PaperInkStroke stroke, PaperInkPoint point)
        => !stroke.PressureEnabled || stroke.Tool == PaperInkTool.Highlighter
            ? stroke.Width
            : stroke.Width * (0.35 + Math.Clamp(point.Pressure, 0, 1) * 0.9);
}
