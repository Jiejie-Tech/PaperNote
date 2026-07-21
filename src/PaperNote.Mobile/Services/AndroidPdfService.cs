using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using Java.IO;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;
using PaperNote.Mobile.Platforms.Android;
using AColor = Android.Graphics.Color;
using ARectF = Android.Graphics.RectF;
using Paint = Android.Graphics.Paint;
using IOPath = System.IO.Path;
using File = System.IO.File;

namespace PaperNote.Mobile.Services;

public sealed class AndroidPdfService
{
    public async Task<IReadOnlyList<NotebookPage>> ImportAsync(FileResult file, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var cachePath = IOPath.Combine(FileSystem.CacheDirectory, $"pdf-import-{Guid.NewGuid():N}.pdf");
        await using (var source = await file.OpenReadAsync())
        await using (var target = File.Create(cachePath))
            await source.CopyToAsync(target, cancellationToken);

        try
        {
            using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(cachePath), ParcelFileMode.ReadOnly)
                ?? throw new InvalidOperationException("无法读取 PDF 文件。");
            using var renderer = new PdfRenderer(descriptor);
            var pages = new List<NotebookPage>(renderer.PageCount);
            for (var index = 0; index < renderer.PageCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var page = renderer.OpenPage(index);
                const int outputWidth = 1260;
                var outputHeight = Math.Max(1, (int)Math.Round(outputWidth * page.Height / (double)page.Width));
                using var bitmap = Bitmap.CreateBitmap(outputWidth, outputHeight, Bitmap.Config.Argb8888!)
                    ?? throw new InvalidOperationException("无法创建 PDF 页面图像。");
                bitmap.EraseColor(AColor.White);
                page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);
                using var stream = new MemoryStream();
                bitmap.Compress(Bitmap.CompressFormat.Png!, 92, stream);
                pages.Add(new NotebookPage
                {
                    Title = $"PDF 第 {index + 1} 页",
                    PaperTemplate = "Blank",
                    PaperColor = "#FFFFFF",
                    BackgroundImageData = Convert.ToBase64String(stream.ToArray()),
                    BackgroundSourceType = "PDF",
                    BackgroundSourceName = file.FileName,
                    BackgroundPageNumber = index + 1
                });
                progress?.Report((index + 1d) / renderer.PageCount);
            }
            return pages;
        }
        finally
        {
            if (File.Exists(cachePath)) File.Delete(cachePath);
        }
    }

    public async Task<string> ExportAndShareAsync(NotebookDocument notebook, IReadOnlyList<NotebookPage>? selectedPages = null, CancellationToken cancellationToken = default)
    {
        var pages = selectedPages ?? notebook.Pages;
        var outputPath = IOPath.Combine(FileSystem.CacheDirectory, $"{Sanitize(notebook.Title)}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        using var document = new PdfDocument();
        var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.Dither);
        try
        {
            for (var index = 0; index < pages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new PdfDocument.PageInfo.Builder(840, 1188, index + 1).Create();
                var pdfPage = document.StartPage(info) ?? throw new InvalidOperationException("Unable to create a PDF page.");
                DrawPage(pdfPage.Canvas!, pages[index], paint);
                document.FinishPage(pdfPage);
            }
            await using (var stream = File.Create(outputPath)) document.WriteTo(stream);
        }
        finally
        {
            paint.Dispose();
        }
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = $"导出 {notebook.Title} PDF",
            File = new ShareFile(outputPath, "application/pdf")
        });
        return outputPath;
    }

    private static void DrawPage(Canvas canvas, NotebookPage page, Paint paint)
    {
        Bitmap? background = null;
        try
        {
            TryDecode(page.BackgroundImageData, out background!);
            AndroidPageRenderer.DrawPage(canvas, page, page.Ink, paint, background);
        }
        finally
        {
            background?.Dispose();
        }
    }

    private static void DrawTemplate(Canvas canvas, string? template, Paint paint)
    {
        if (string.Equals(template, "Blank", StringComparison.OrdinalIgnoreCase)) return;
        paint.Color = AColor.Argb(70, 125, 145, 170);
        paint.StrokeWidth = 1;
        if (string.Equals(template, "Dotted", StringComparison.OrdinalIgnoreCase))
        {
            paint.SetStyle(Paint.Style.Fill);
            for (var y = 36f; y < 1188; y += 36)
                for (var x = 36f; x < 840; x += 36)
                    canvas.DrawCircle(x, y, 1.25f, paint);
            return;
        }
        paint.SetStyle(Paint.Style.Stroke);
        var step = string.Equals(template, "Grid", StringComparison.OrdinalIgnoreCase) ? 36f : 42f;
        for (var y = step; y < 1188; y += step) canvas.DrawLine(0, y, 840, y, paint);
        if (string.Equals(template, "Grid", StringComparison.OrdinalIgnoreCase))
            for (var x = step; x < 840; x += step) canvas.DrawLine(x, 0, x, 1188, paint);
    }

    private static void DrawStroke(Canvas canvas, PaperInkStroke stroke, Paint paint)
    {
        if (stroke.Points.Count == 0) return;
        paint.SetStyle(Paint.Style.Stroke);
        paint.StrokeCap = Paint.Cap.Round;
        paint.StrokeJoin = Paint.Join.Round;
        paint.Color = ParseColor(stroke.Color, AColor.Rgb(29, 37, 48));
        paint.Alpha = (int)(255 * Math.Clamp(stroke.Opacity, 0.01, 1));
        if (stroke.Points.Count == 1)
        {
            paint.SetStyle(Paint.Style.Fill);
            var point = stroke.Points[0];
            canvas.DrawCircle((float)point.X, (float)point.Y, (float)(Width(stroke, point) / 2), paint);
        }
        else
        {
            for (var i = 1; i < stroke.Points.Count; i++)
            {
                var a = stroke.Points[i - 1];
                var b = stroke.Points[i];
                paint.StrokeWidth = (float)((Width(stroke, a) + Width(stroke, b)) / 2);
                canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, paint);
            }
        }
        paint.Alpha = 255;
    }

    private static void DrawObject(Canvas canvas, PageObject item, Paint paint)
    {
        paint.Alpha = (int)(255 * Math.Clamp(item.Opacity, 0.1, 1));
        if (item.Kind == "Text")
        {
            paint.SetStyle(Paint.Style.Fill);
            paint.TextSize = (float)item.FontSize;
            paint.Color = ParseColor(item.StrokeColor, AColor.DarkGray);
            canvas.DrawText(item.Text ?? string.Empty, (float)item.X, (float)(item.Y + item.FontSize), paint);
        }
        else if (item.Kind == "Image" && TryDecode(item.ImageData, out var image))
        {
            using (image) canvas.DrawBitmap(image, null, new ARectF((float)item.X, (float)item.Y, (float)(item.X + item.Width), (float)(item.Y + item.Height)), paint);
        }
        else
        {
            paint.SetStyle(Paint.Style.Stroke);
            paint.StrokeWidth = (float)item.StrokeThickness;
            paint.Color = ParseColor(item.StrokeColor, AColor.Rgb(49, 87, 213));
            var rect = new ARectF((float)item.X, (float)item.Y, (float)(item.X + item.Width), (float)(item.Y + item.Height));
            if (item.ShapeKind == "Ellipse") canvas.DrawOval(rect, paint); else canvas.DrawRect(rect, paint);
        }
        paint.Alpha = 255;
    }

    private static double Width(PaperInkStroke stroke, PaperInkPoint point)
        => !stroke.PressureEnabled || stroke.Tool == PaperInkTool.Highlighter ? stroke.Width : stroke.Width * (0.35 + point.Pressure * 0.9);

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

    private static string Sanitize(string value)
    {
        foreach (var invalid in IOPath.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "PaperNote" : value.Trim();
    }
}
