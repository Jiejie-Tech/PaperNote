using System.IO;
using PaperNote.Core.Services;
using PDFtoImage;
using SkiaSharp;

namespace PaperNote.Desktop.Services;

public sealed record ImportedPdfPage(int PageNumber, string ImageData, bool FromCache = false);

public static class PdfImportService
{
    public const long MaximumFileSizeBytes = 200L * 1024 * 1024;
    public const int MaximumPageCount = PdfPageRangeService.MaximumImportPageCount;

    public static int GetPageCount(string filePath)
    {
        ValidatePdf(filePath);
        try
        {
            using var stream = File.OpenRead(filePath);
            var pageCount = Conversion.GetPageCount(stream, leaveOpen: false, password: null);
            if (pageCount <= 0) throw new InvalidDataException("PDF 中没有可导入的页面。");
            return pageCount;
        }
        catch (Exception exception) when (exception is not ArgumentException and not FileNotFoundException and not InvalidDataException)
        {
            throw new InvalidDataException("无法读取这个 PDF，文件可能已损坏、受密码保护或格式不受支持。", exception);
        }
    }

    public static IReadOnlyList<int> ParsePageSelection(string? selection, int pageCount)
        => PdfPageRangeService.Parse(selection, pageCount, MaximumPageCount);

    public static IReadOnlyList<ImportedPdfPage> Import(string filePath, int targetWidth = 840, int targetHeight = 1188)
    {
        var pageCount = GetPageCount(filePath);
        if (pageCount > MaximumPageCount)
            throw new InvalidDataException($"这个 PDF 共 {pageCount} 页，一次最多导入 {MaximumPageCount} 页，请选择页码范围。");
        return Import(filePath, Enumerable.Range(1, pageCount).ToArray(), targetWidth, targetHeight);
    }

    public static IReadOnlyList<ImportedPdfPage> Import(
        string filePath,
        IReadOnlyList<int> pageNumbers,
        int targetWidth = 840,
        int targetHeight = 1188)
        => ImportAsync(filePath, pageNumbers, targetWidth, targetHeight).GetAwaiter().GetResult();

    public static async Task<IReadOnlyList<ImportedPdfPage>> ImportAsync(
        string filePath,
        IReadOnlyList<int> pageNumbers,
        int targetWidth = 840,
        int targetHeight = 1188,
        string? cacheDirectory = null,
        IProgress<PdfImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePdf(filePath);
        if (targetWidth <= 0 || targetHeight <= 0) throw new ArgumentOutOfRangeException(nameof(targetWidth), "页面尺寸必须大于零。");
        if (pageNumbers.Count == 0) throw new ArgumentException("请至少选择一页 PDF。", nameof(pageNumbers));
        if (pageNumbers.Count > MaximumPageCount) throw new InvalidDataException($"一次最多导入 {MaximumPageCount} 页 PDF。");

        var pageCount = await Task.Run(() => GetPageCount(filePath), cancellationToken);
        var requestedPages = pageNumbers.Distinct().OrderBy(page => page).ToArray();
        if (requestedPages.Any(page => page < 1 || page > pageCount))
            throw new InvalidDataException($"导入页码必须在 1 至 {pageCount} 之间。");

        var cache = new PdfImportCacheService(cacheDirectory ?? GetDefaultCacheDirectory());
        PdfImportJobManifest? job = null;
        try
        {
            job = await cache.PrepareAsync(
                filePath,
                Path.GetFileName(filePath),
                requestedPages,
                pageCount,
                targetWidth,
                targetHeight,
                progress,
                cancellationToken);

            var options = new RenderOptions
            {
                Width = targetWidth,
                Height = null,
                WithAspectRatio = true,
                WithAnnotations = true,
                WithFormFill = true,
                BackgroundColor = SKColors.White
            };

            var result = new List<ImportedPdfPage>(requestedPages.Length);
            var completed = 0;
            using var pdfStream = new FileStream(cache.GetSourcePath(job), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.RandomAccess);
            foreach (var pageNumber in requestedPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] imageBytes;
                var fromCache = cache.TryReadPage(job, pageNumber, out imageBytes);
                if (!fromCache)
                {
                    progress?.Report(new PdfImportProgress("渲染", completed, requestedPages.Length, pageNumber, false, $"正在渲染第 {pageNumber} 页…"));
                    imageBytes = await Task.Run(() => RenderPage(pdfStream, pageNumber, targetWidth, targetHeight, options), cancellationToken);
                    await cache.SavePageAsync(job, pageNumber, imageBytes, ".jpg", cancellationToken);
                }

                result.Add(new ImportedPdfPage(pageNumber, Convert.ToBase64String(imageBytes), fromCache));
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
            return result;
        }
        catch (OperationCanceledException)
        {
            if (job is not null) await cache.MarkCancelledAsync(job, CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is not ArgumentException and not FileNotFoundException and not InvalidDataException)
        {
            if (job is not null) await cache.MarkFailedAsync(job, exception, CancellationToken.None);
            throw new InvalidDataException("无法读取这个 PDF，文件可能已损坏、受密码保护或格式不受支持。已完成页面会保留供下次续接。", exception);
        }
        catch (Exception exception)
        {
            if (job is not null) await cache.MarkFailedAsync(job, exception, CancellationToken.None);
            throw;
        }
    }

    public static string GetDefaultCacheDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperNote", "PdfImportCache");

    private static byte[] RenderPage(FileStream pdfStream, int pageNumber, int targetWidth, int targetHeight, RenderOptions options)
    {
        lock (pdfStream)
        {
            using var renderedPage = Conversion.ToImage(pdfStream, pageNumber - 1, leaveOpen: true, password: null, options);
            using var fitted = FitToPage(renderedPage, targetWidth, targetHeight);
            using var encoded = fitted.Encode(SKEncodedImageFormat.Jpeg, 88)
                ?? throw new InvalidDataException($"第 {pageNumber} 页图像编码失败。");
            return encoded.ToArray();
        }
    }

    private static void ValidatePdf(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("请选择 PDF 文件。", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("找不到要导入的 PDF 文件。", filePath);
        if (!string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("只能导入 PDF 文件。");
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaximumFileSizeBytes) throw new InvalidDataException("PDF 文件不能超过 200 MB。");
        if (fileInfo.Length == 0) throw new InvalidDataException("PDF 文件为空。");
    }

    private static SKBitmap FitToPage(SKBitmap source, int targetWidth, int targetHeight)
    {
        var output = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);
        var scale = Math.Min(targetWidth / (double)Math.Max(1, source.Width), targetHeight / (double)Math.Max(1, source.Height));
        var width = (float)Math.Max(1, source.Width * scale);
        var height = (float)Math.Max(1, source.Height * scale);
        var left = (targetWidth - width) / 2f;
        var top = (targetHeight - height) / 2f;
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(source, new SKRect(left, top, left + width, top + height), paint);
        canvas.Flush();
        return output;
    }
}
