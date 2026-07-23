using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using Java.IO;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Mobile.Platforms.Android;
using AColor = Android.Graphics.Color;
using ARectF = Android.Graphics.RectF;
using Paint = Android.Graphics.Paint;
using IOPath = System.IO.Path;
using File = System.IO.File;

namespace PaperNote.Mobile.Services;

public sealed class PreparedAndroidPdfImport : IAsyncDisposable
{
    public required string StagingPath { get; init; }
    public required string SourceName { get; init; }
    public required int PageCount { get; init; }
    public required long Length { get; init; }

    public ValueTask DisposeAsync()
    {
        try { if (File.Exists(StagingPath)) File.Delete(StagingPath); } catch { }
        return ValueTask.CompletedTask;
    }
}

public sealed record AndroidPdfImportPage(NotebookPage Page, bool FromCache);

public sealed record AndroidPdfImportResult(
    string SourceName,
    string SourceFingerprint,
    IReadOnlyList<AndroidPdfImportPage> Pages,
    PdfDocumentContent Content);

public sealed class AndroidPdfService
{
    public const long MaximumFileSizeBytes = 200L * 1024 * 1024;

    public async Task<PreparedAndroidPdfImport> PrepareImportAsync(
        FileResult file,
        IProgress<PdfImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        var cachePath = IOPath.Combine(FileSystem.CacheDirectory, $"pdf-stage-{Guid.NewGuid():N}.pdf");
        long copied = 0;
        try
        {
            await using (var source = await file.OpenReadAsync())
            await using (var target = new FileStream(cachePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            {
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    copied += read;
                    if (copied > MaximumFileSizeBytes) throw new InvalidDataException("PDF 文件不能超过 200 MB。");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    var total = source.CanSeek ? source.Length : 0;
                    var percent = total <= 0 ? 0 : Math.Clamp(copied / (double)total, 0, 1);
                    progress?.Report(new PdfImportProgress("复制", (int)Math.Round(percent * 100), 100, 0, false, $"正在读取 PDF… {Math.Round(copied / 1024d / 1024d, 1)} MB"));
                }
                await target.FlushAsync(cancellationToken);
            }
            if (copied == 0) throw new InvalidDataException("PDF 文件为空。");

            using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(cachePath), ParcelFileMode.ReadOnly)
                ?? throw new InvalidOperationException("无法读取 PDF 文件。");
            using var renderer = new PdfRenderer(descriptor);
            if (renderer.PageCount <= 0) throw new InvalidDataException("PDF 中没有可导入的页面。");
            return new PreparedAndroidPdfImport
            {
                StagingPath = cachePath,
                SourceName = file.FileName,
                PageCount = renderer.PageCount,
                Length = copied
            };
        }
        catch
        {
            try { if (File.Exists(cachePath)) File.Delete(cachePath); } catch { }
            throw;
        }
    }

    public async Task<IReadOnlyList<NotebookPage>> ImportAsync(FileResult file, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var prepared = await PrepareImportAsync(file, cancellationToken: cancellationToken);
        var pages = PdfPageRangeService.Parse("all", prepared.PageCount);
        IProgress<PdfImportProgress>? detailed = progress is null
            ? null
            : new Progress<PdfImportProgress>(item => progress.Report(item.Fraction));
        var imported = await ImportAsync(prepared, pages, detailed, cancellationToken);
        return imported.Select(item => item.Page).ToArray();
    }

    public async Task<IReadOnlyList<AndroidPdfImportPage>> ImportAsync(
        PreparedAndroidPdfImport prepared,
        IReadOnlyList<int> pageNumbers,
        IProgress<PdfImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => (await ImportDocumentAsync(prepared, pageNumbers, progress, cancellationToken)).Pages;

    public async Task<AndroidPdfImportResult> ImportDocumentAsync(
        PreparedAndroidPdfImport prepared,
        IReadOnlyList<int> pageNumbers,
        IProgress<PdfImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var requestedPages = pageNumbers.Distinct().OrderBy(page => page).ToArray();
        if (requestedPages.Length == 0 || requestedPages.Any(page => page < 1 || page > prepared.PageCount))
            throw new InvalidDataException("PDF 页码范围无效。");
        if (requestedPages.Length > PdfPageRangeService.MaximumImportPageCount)
            throw new InvalidDataException($"一次最多导入 {PdfPageRangeService.MaximumImportPageCount} 页。");

        var cache = new PdfImportCacheService(IOPath.Combine(FileSystem.CacheDirectory, "PdfImportCache"));
        PdfImportJobManifest? job = null;
        try
        {
            job = await cache.PrepareAsync(
                prepared.StagingPath,
                prepared.SourceName,
                requestedPages,
                prepared.PageCount,
                840,
                1188,
                progress,
                cancellationToken);

            PdfDocumentContent extractedContent;
            try
            {
                extractedContent = await PdfDocumentContentService.ExtractAsync(cache.GetSourcePath(job), requestedPages, progress, cancellationToken);
            }
            catch (Exception exception) when (exception is not System.OperationCanceledException)
            {
                extractedContent = new PdfDocumentContent(
                    new Dictionary<int, PdfExtractedPage>(),
                    Array.Empty<PdfExtractedOutline>(),
                    new[] { $"PDF 文本提取失败：{exception.Message}" });
            }

            using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(cache.GetSourcePath(job)), ParcelFileMode.ReadOnly)
                ?? throw new InvalidOperationException("无法读取 PDF 文件。");
            using var renderer = new PdfRenderer(descriptor);
            var pages = new List<AndroidPdfImportPage>(requestedPages.Length);
            var completed = 0;
            foreach (var pageNumber in requestedPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] imageBytes;
                var fromCache = cache.TryReadPage(job, pageNumber, out imageBytes);
                if (!fromCache)
                {
                    progress?.Report(new PdfImportProgress("渲染", completed, requestedPages.Length, pageNumber, false, $"正在渲染第 {pageNumber} 页…"));
                    imageBytes = RenderPage(renderer, pageNumber - 1);
                    await cache.SavePageAsync(job, pageNumber, imageBytes, ".jpg", cancellationToken);
                }

                pages.Add(new AndroidPdfImportPage(new NotebookPage
                {
                    Title = $"PDF 第 {pageNumber} 页",
                    PaperTemplate = "Blank",
                    PaperColor = "#FFFFFF",
                    BackgroundImageData = Convert.ToBase64String(imageBytes),
                    BackgroundSourceType = "PDF",
                    BackgroundSourceName = prepared.SourceName,
                    BackgroundPageNumber = pageNumber
                }, fromCache));
                completed++;
                progress?.Report(new PdfImportProgress(
                    fromCache ? "缓存" : "渲染",
                    completed,
                    requestedPages.Length,
                    pageNumber,
                    fromCache,
                    fromCache ? $"已从缓存恢复第 {pageNumber} 页" : $"已完成第 {pageNumber} 页"));
            }

            await cache.MarkCompletedAsync(job, cancellationToken);
            return new AndroidPdfImportResult(job.SourceName, job.SourceFingerprint, pages, extractedContent);
        }
        catch (System.OperationCanceledException)
        {
            if (job is not null) await cache.MarkCancelledAsync(job, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            if (job is not null) await cache.MarkFailedAsync(job, exception, CancellationToken.None);
            throw;
        }
    }

    private static byte[] RenderPage(PdfRenderer renderer, int zeroBasedPageIndex)
    {
        using var page = renderer.OpenPage(zeroBasedPageIndex);
        const int outputWidth = 840;
        const int outputHeight = 1188;
        using var bitmap = Bitmap.CreateBitmap(outputWidth, outputHeight, Bitmap.Config.Argb8888!)
            ?? throw new InvalidOperationException("无法创建 PDF 页面图像。");
        bitmap.EraseColor(AColor.White);
        var scale = Math.Min(outputWidth / (double)Math.Max(1, page.Width), outputHeight / (double)Math.Max(1, page.Height));
        var width = Math.Max(1, (int)Math.Round(page.Width * scale));
        var height = Math.Max(1, (int)Math.Round(page.Height * scale));
        var left = (outputWidth - width) / 2;
        var top = (outputHeight - height) / 2;
        using var destination = new Android.Graphics.Rect(left, top, left + width, top + height);
        page.Render(bitmap, destination, null, PdfRenderMode.ForDisplay);
        using var stream = new MemoryStream();
        if (!bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 88, stream))
            throw new InvalidDataException("PDF 页面图像编码失败。");
        return stream.ToArray();
    }
    public async Task<string?> ExportSelectionAndShareAsync(
        NotebookPage page,
        IReadOnlyCollection<Guid> strokeIds,
        IReadOnlyCollection<Guid> objectIds,
        CancellationToken cancellationToken = default)
    {
        var selection = SelectionExportService.Create(page, strokeIds, objectIds);
        if (selection is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        var outputPath = IOPath.Combine(FileSystem.CacheDirectory, $"PaperNote-Selection-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using var bitmap = Bitmap.CreateBitmap(840, 1188, Bitmap.Config.Argb8888!)
            ?? throw new InvalidOperationException("无法创建选区图像。");
        using var canvas = new Canvas(bitmap);
        using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.Dither);
        AndroidPageRenderer.DrawPage(canvas, selection.Page, selection.Page.Ink, paint, null);
        var left = Math.Clamp((int)Math.Floor(selection.X), 0, bitmap.Width - 1);
        var top = Math.Clamp((int)Math.Floor(selection.Y), 0, bitmap.Height - 1);
        var width = Math.Clamp((int)Math.Ceiling(selection.Width), 1, bitmap.Width - left);
        var height = Math.Clamp((int)Math.Ceiling(selection.Height), 1, bitmap.Height - top);
        using var cropped = Bitmap.CreateBitmap(bitmap, left, top, width, height)
            ?? throw new InvalidOperationException("无法裁剪选区图像。");
        await using (var stream = File.Create(outputPath))
        {
            if (!cropped.Compress(Bitmap.CompressFormat.Png!, 100, stream))
                throw new InvalidDataException("选区 PNG 编码失败。");
        }
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "导出 PaperNote 选区",
            File = new ShareFile(outputPath, "image/png")
        });
        return outputPath;
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
